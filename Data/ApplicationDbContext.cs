using Employee_Leave.Models;
using Microsoft.EntityFrameworkCore;

namespace Employee_Leave.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Employee> Employees { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }

        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Employee>()
                .HasIndex(e => new { e.Name, e.Role })
                .IsUnique();

            base.OnModelCreating(modelBuilder);
        }
    }
}
