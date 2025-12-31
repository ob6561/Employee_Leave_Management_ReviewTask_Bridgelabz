using System.ComponentModel.DataAnnotations;

namespace Employee_Leave.Models
{
    public class RefreshToken
    {
        [Key]
        public int Id { get; set; }

        public string Token { get; set; } = string.Empty;
        public DateTime Expires { get; set; }

        public bool IsRevoked { get; set; }

        public int EmployeeId { get; set; }
        public Employee Employee { get; set; }
    }
}
