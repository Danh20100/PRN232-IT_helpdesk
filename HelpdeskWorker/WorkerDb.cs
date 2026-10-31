using Microsoft.EntityFrameworkCore;

namespace HelpdeskWorker;

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? TicketId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
}

public class WorkerDb(DbContextOptions<WorkerDb> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Notification>(e =>
        {
            e.ToTable("Notifications");
            e.Property(x => x.Id).HasColumnName("NotificationId");
        });
    }
}
