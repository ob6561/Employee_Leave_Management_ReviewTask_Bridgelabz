using Employee_Leave.Data;
using Employee_Leave.DTOs;
using Employee_Leave.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Employee_Leave.Controllers
{
    [ApiController]
    [Route("api")]
    public class EmployeeController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public EmployeeController(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // ====================== EMPLOYEE ======================

        [HttpPost("employee/add")]
        public IActionResult AddEmployee(EmployeeCreateDto dto)
        {
            bool exists = _context.Employees.Any(e =>
                e.Name.ToLower() == dto.Name.ToLower() &&
                e.Role.ToLower() == dto.Role.ToLower());

            if (exists)
                return BadRequest("Employee already exists");

            var employee = new Employee
            {
                Name = dto.Name.Trim(),
                Role = dto.Role.Trim(),
                IsActive = dto.IsActive,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
            };

            _context.Employees.Add(employee);
            _context.SaveChanges();

            return Ok("Employee added successfully");
        }

        [HttpGet("employee")]
        public IActionResult GetEmployees()
        {
            return Ok(_context.Employees.ToList());
        }

        [HttpPut("employee/deactivate/{employeeId}")]
        public IActionResult DeactivateEmployee(int employeeId)
        {
            var employee = _context.Employees.Find(employeeId);

            if (employee == null)
                return NotFound("Employee not found");

            if (!employee.IsActive)
                return BadRequest("Employee already inactive");

            employee.IsActive = false;
            _context.SaveChanges();

            return Ok("Employee deactivated successfully");
        }

        // ====================== LEAVE ======================

        [Authorize]
        [HttpPost("leave/apply")]
        public IActionResult ApplyLeave(ApplyLeaveDto dto)
        {
            var employee = _context.Employees.Find(dto.EmployeeId);

            if (employee == null || !employee.IsActive)
                return BadRequest("Invalid or inactive employee");

            if (dto.FromDate >= dto.ToDate)
                return BadRequest("FromDate must be before ToDate");

            bool overlapping = _context.LeaveRequests.Any(l =>
                l.EmployeeId == dto.EmployeeId &&
                l.Status == "Approved" &&
                dto.FromDate <= l.ToDate &&
                dto.ToDate >= l.FromDate);

            if (overlapping)
                return BadRequest("Overlapping leave already approved");

            var leave = new LeaveRequest
            {
                EmployeeId = dto.EmployeeId,
                FromDate = dto.FromDate,
                ToDate = dto.ToDate,
                Status = "Pending"
            };

            _context.LeaveRequests.Add(leave);
            _context.SaveChanges();

            return Ok("Leave applied successfully");
        }

        [HttpGet("leave")]
        public IActionResult GetAllLeaves()
        {
            return Ok(_context.LeaveRequests.ToList());
        }

        [Authorize(Roles = "Manager")]
        [HttpPut("leave/approve-reject/{leaveId}")]
        public IActionResult ApproveRejectLeave(int leaveId, [FromQuery] string action)
        {
            var leave = _context.LeaveRequests.Find(leaveId);

            if (leave == null)
                return NotFound("Leave request not found");

            if (leave.Status != "Pending")
                return BadRequest("Leave already processed");

            action = action.Trim().ToLower();

            if (action != "approve" && action != "reject")
                return BadRequest("Invalid action");

            leave.Status = action == "approve" ? "Approved" : "Rejected";
            _context.SaveChanges();

            return Ok($"Leave {leave.Status} successfully");
        }

        [Authorize]
        [HttpPut("leave/cancel/{leaveId}")]
        public IActionResult CancelLeave(int leaveId)
        {
            int employeeId = int.Parse(User.FindFirst("EmployeeId")!.Value);

            var leave = _context.LeaveRequests.FirstOrDefault(l =>
                l.LeaveRequestId == leaveId &&
                l.EmployeeId == employeeId);

            if (leave == null)
                return NotFound("Leave request not found");

            if (leave.Status != "Pending")
                return BadRequest("Only pending leave can be cancelled");

            leave.Status = "Cancelled";
            _context.SaveChanges();

            return Ok("Leave cancelled successfully");
        }

        // ====================== REPORTS ======================

        [Authorize(Roles = "HR")]
        [HttpGet("leave/report/total-leaves")]
        public IActionResult GetTotalLeavesPerEmployee()
        {
            var report = _context.LeaveRequests
                .Where(l => l.Status == "Approved")
                .GroupBy(l => l.EmployeeId)
                .Select(g => new
                {
                    EmployeeId = g.Key,
                    TotalLeaves = g.Count()
                })
                .ToList();

            return Ok(report);
        }

        [Authorize(Roles = "HR")]
        [HttpGet("leave/report/status-summary")]
        public IActionResult GetLeaveStatusSummary()
        {
            var report = _context.LeaveRequests
                .GroupBy(l => l.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToList();

            return Ok(report);
        }

        // ====================== AUTH ======================

        [HttpPost("auth/login")]
        public IActionResult Login(LoginDto dto)
        {
            var employee = _context.Employees.FirstOrDefault(e =>
                e.Name.ToLower() == dto.Name.Trim().ToLower() &&
                e.IsActive);

            if (employee == null)
                return Unauthorized("Invalid credentials");

            bool validPassword = BCrypt.Net.BCrypt.Verify(
                dto.Password,
                employee.PasswordHash);

            if (!validPassword)
                return Unauthorized("Invalid credentials");

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, employee.Name),
                new Claim(ClaimTypes.Role, employee.Role),
                new Claim("EmployeeId", employee.EmployeeId.ToString())
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!)
            );

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(
                    Convert.ToDouble(_configuration["Jwt:DurationInMinutes"])
                ),
                signingCredentials: new SigningCredentials(
                    key, SecurityAlgorithms.HmacSha256)
            );

            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

            var refreshToken = GenerateRefreshToken();

            _context.RefreshTokens.Add(new RefreshToken
            {
                Token = refreshToken,
                EmployeeId = employee.EmployeeId,
                Expires = DateTime.Now.AddDays(7)
            });

            _context.SaveChanges();

            return Ok(new
            {
                accessToken,
                refreshToken
            });
        }

        [HttpPost("auth/refresh")]
        public IActionResult RefreshToken(RefreshTokenDto dto)
        {
            var storedToken = _context.RefreshTokens
                .Include(r => r.Employee)
                .FirstOrDefault(r =>
                    r.Token == dto.RefreshToken &&
                    !r.IsRevoked &&
                    r.Expires > DateTime.Now);

            if (storedToken == null)
                return Unauthorized("Invalid refresh token");

            var employee = storedToken.Employee;

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, employee.Name),
                new Claim(ClaimTypes.Role, employee.Role),
                new Claim("EmployeeId", employee.EmployeeId.ToString())
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!)
            );

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(15),
                signingCredentials: new SigningCredentials(
                    key, SecurityAlgorithms.HmacSha256)
            );

            return Ok(new
            {
                accessToken = new JwtSecurityTokenHandler().WriteToken(token)
            });
        }

        // ====================== HELPERS ======================

        private string GenerateRefreshToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        }
    }
}
