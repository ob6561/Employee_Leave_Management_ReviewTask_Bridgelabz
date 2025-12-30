using System.ComponentModel.DataAnnotations;

namespace Employee_Leave.Models
{
    public class Employee
    {
        [Key]
        public int EmployeeId { get; set; }
        public string Name { get; set; }
        public string Role { get; set; } 
        public bool IsActive { get; set; }
    }
}
