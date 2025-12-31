using Employee_Leave.Data;
using Employee_Leave.DTOs;
using Employee_Leave.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Employee_Leave.Controllers
{
    [ApiController]
    [Route("api")]
    public class EmployeeController : ControllerBase
    {
        private readonly ApplicationDbContext con;
        private readonly IConfiguration _configuration;

        public EmployeeController(
            ApplicationDbContext context,
            IConfiguration configuration)
        {
            con = context;
            _configuration = configuration;
        }

        // ---------------- EMPLOYEE ----------------

        [HttpPost("employee/add")]
        public IActionResult AddEmployee(EmployeeCreateDto dto)
        {
            bool exists = con.Employees.Any(e =>
                e.Name == dto.Name &&
                e.Role == dto.Role);

            if (exists)
                return BadRequest("Employee already exists");

            var employee = new Employee
            {
                Name = dto.Name,
                Role = dto.Role,
                IsActive = dto.IsActive
            };

            con.Employees.Add(employee);
            con.SaveChanges();

            return Ok("Employee added successfully");
        }

        [HttpGet("employee")]
        public IActionResult GetEmployees()
        {
            return Ok(con.Employees.ToList());
        }

        [HttpPut("employee/deactivate/{employeeId}")]
        public IActionResult DeactivateEmployee(int employeeId)
        {
            var employee = con.Employees.Find(employeeId);

            if (employee == null)
                return NotFound("Employee not found");

            if (!employee.IsActive)
                return BadRequest("Employee already inactive");

            employee.IsActive = false;
            con.SaveChanges();

            return Ok("Employee deactivated successfully");
        }

        // ---------------- LEAVE ----------------
        [Authorize]
        [HttpPost("leave/apply")]
        public IActionResult ApplyLeave(ApplyLeaveDto dto)
        {
            var employee = con.Employees.Find(dto.EmployeeId);

            if (employee == null || !employee.IsActive)
                return BadRequest("Invalid or inactive employee");

            if (dto.FromDate >= dto.ToDate)
                return BadRequest("FromDate must be before ToDate");

            bool overlapping = con.LeaveRequests.Any(l =>
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

            con.LeaveRequests.Add(leave);
            con.SaveChanges();

            return Ok("Leave applied successfully");
        }

        [HttpGet("leave")]
        public IActionResult GetAllLeaves()
        {
            return Ok(con.LeaveRequests.ToList());
        }

        [Authorize(Roles = "Manager")]
        [HttpPut("leave/approve-reject/{leaveId}")]
        public IActionResult ApproveRejectLeave(
            int leaveId,
            [FromQuery] string action,
            [FromHeader] string role)
        {
            if (role != "Manager")
                return Unauthorized("Only managers can approve or reject leave");

            var leave = con.LeaveRequests.Find(leaveId);

            if (leave == null)
                return NotFound("Leave request not found");

            if (leave.Status != "Pending")
                return BadRequest("Leave already processed");

            if (action != "Approve" && action != "Reject")
                return BadRequest("Invalid action");

            leave.Status = action == "Approve" ? "Approved" : "Rejected";
            con.SaveChanges();

            return Ok($"Leave {leave.Status} successfully");
        }

        [HttpPut("leave/cancel/{leaveId}")]
        public IActionResult CancelLeave(
            int leaveId,
            [FromHeader] int employeeId)
        {
            var leave = con.LeaveRequests.FirstOrDefault(l =>
                l.LeaveRequestId == leaveId &&
                l.EmployeeId == employeeId);

            if (leave == null)
                return NotFound("Leave request was not found");

            if (leave.Status != "Pending")
                return BadRequest("Only pending leave can be cancelled");

            leave.Status = "Cancelled";
            con.SaveChanges();

            return Ok("Leave cancelled successfully");
        }

        // ---------------- REPORTS ----------------

        [Authorize(Roles = "HR")]
        [HttpGet("leave/report/total-leaves")]
        public IActionResult GetTotalLeavesPerEmployee()
        {
            var report = con.LeaveRequests
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

        [HttpGet("leave/report/status-summary")]
        public IActionResult GetLeaveStatusSummary()
        {
            var report = con.LeaveRequests
                .GroupBy(l => l.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToList();

            return Ok(report);
        }

        // ---------------- AUTH ----------------

        [HttpPost("auth/login")]
        public IActionResult Login(LoginDto dto)
        {
            var employee = con.Employees.FirstOrDefault(e =>
                e.Name == dto.Name &&
                e.Role == dto.Role &&
                e.IsActive);

            if (employee == null)
                return Unauthorized("Invalid credentials");

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, employee.Name),
                new Claim(ClaimTypes.Role, employee.Role),
                new Claim("EmployeeId", employee.EmployeeId.ToString())
            };

            var jwtKey = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("JWT Key missing");

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            );

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(
                    Convert.ToDouble(_configuration["Jwt:DurationInMinutes"])
                ),
                signingCredentials: creds
            );

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token)
            });
        }
    }
}
