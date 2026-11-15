using Microsoft.EntityFrameworkCore;

namespace HelpdeskWorker;

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? TicketId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class WorkerTicket
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int CreatedById { get; set; }
    public int? AssignedToId { get; set; }
    public int Status { get; set; }
    public DateTime? DueDate { get; set; }
}

public class WorkerUser
{
    public int Id { get; set; }
    public int Role { get; set; }
    public bool IsActive { get; set; }
}

public class WorkerDb(DbContextOptions<WorkerDb> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<WorkerTicket> Tickets => Set<WorkerTicket>();
    public DbSet<WorkerUser> Users => Set<WorkerUser>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Notification>(e =>
        {
            e.ToTable("Notifications");
            e.Property(x => x.Id).HasColumnName("NotificationId");
        });
        b.Entity<WorkerTicket>(e =>
        {
            e.ToTable("Tickets");
            e.Property(x => x.Id).HasColumnName("TicketId");
        });
        b.Entity<WorkerUser>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.Id).HasColumnName("UserId");
        });
    }
}
