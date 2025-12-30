namespace Employee_Leave.Models
{
    public class LeaveRequest
    {
        public int LeaveRequestId { get; set; }
        public required int EmployeeId { get; set; }

        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        public required string Status { get; set; }
    }
}
