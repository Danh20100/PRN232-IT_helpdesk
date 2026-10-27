namespace HelpdeskApi.Data;

/// <summary>Database First: DB do db/init.sql tạo, EF Core chỉ map tên bảng/cột (không tạo/migrate bảng).</summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> Comments => Set<TicketComment>();
    public DbSet<TicketStatusHistory> StatusHistories => Set<TicketStatusHistory>();
    public DbSet<TicketAssignmentHistory> AssignmentHistories => Set<TicketAssignmentHistory>();
    public DbSet<TicketFeedback> Feedbacks => Set<TicketFeedback>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.Id).HasColumnName("UserId");
            e.Property(x => x.Role).HasConversion<string>();
        });

        b.Entity<Category>(e =>
        {
            e.ToTable("Categories");
            e.Property(x => x.Id).HasColumnName("CategoryId");
            e.Property(x => x.Name).HasColumnName("CategoryName");
        });

        b.Entity<Ticket>(e =>
        {
            e.ToTable("Tickets");
            e.Property(x => x.Id).HasColumnName("TicketId");
            e.Property(x => x.Code).HasColumnName("TicketCode");
            e.Property(x => x.CreatedById).HasColumnName("CreatedBy");
            e.Property(x => x.AssignedToId).HasColumnName("AssignedTo");
            e.Property(x => x.Priority).HasConversion<byte>();
            e.Property(x => x.Status).HasConversion<byte>();
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById);
            e.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToId);
        });

        b.Entity<TicketComment>(e =>
        {
            e.ToTable("TicketComments");
            e.Property(x => x.Id).HasColumnName("CommentId");
        });

        b.Entity<TicketStatusHistory>(e =>
        {
            e.ToTable("TicketStatusHistories");
            e.Property(x => x.Id).HasColumnName("HistoryId");
            e.Property(x => x.ChangedById).HasColumnName("ChangedBy");
            e.Property(x => x.OldStatus).HasConversion<byte>();
            e.Property(x => x.NewStatus).HasConversion<byte>();
        });

        b.Entity<TicketAssignmentHistory>(e =>
        {
            e.ToTable("TicketAssignmentHistories");
            e.Property(x => x.Id).HasColumnName("AssignmentId");
            e.Property(x => x.AssignedById).HasColumnName("AssignedBy");
        });

        b.Entity<TicketFeedback>(e =>
        {
            e.ToTable("TicketFeedbacks");
            e.Property(x => x.Id).HasColumnName("FeedbackId");
            e.Property(x => x.Rating).HasConversion<byte>();
        });

        // Khớp DB: ON DELETE NO ACTION
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
