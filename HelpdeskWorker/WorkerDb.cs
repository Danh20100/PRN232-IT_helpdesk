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
    public byte Status { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class WorkerStatusHistory
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public byte OldStatus { get; set; }
    public byte NewStatus { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class WorkerUser
{
    public int Id { get; set; }
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
}

public class WorkerDb(DbContextOptions<WorkerDb> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<WorkerTicket> Tickets => Set<WorkerTicket>();
    public DbSet<WorkerStatusHistory> StatusHistories => Set<WorkerStatusHistory>();
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
            e.Property(x => x.Code).HasColumnName("TicketCode");
            e.Property(x => x.CreatedById).HasColumnName("CreatedBy");
            e.Property(x => x.AssignedToId).HasColumnName("AssignedTo");
        });
        b.Entity<WorkerStatusHistory>(e =>
        {
            e.ToTable("TicketStatusHistories");
            e.Property(x => x.Id).HasColumnName("HistoryId");
        });
        b.Entity<WorkerUser>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.Id).HasColumnName("UserId");
        });
    }
}
