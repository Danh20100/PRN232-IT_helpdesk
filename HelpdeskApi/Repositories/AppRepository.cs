namespace HelpdeskApi.Repositories;

/// <summary>Lớp truy cập dữ liệu duy nhất. Service không giữ AppDbContext.</summary>
public class AppRepository(AppDbContext db)
{
    public IQueryable<User> Users => db.Users;
    public IQueryable<Category> Categories => db.Categories;
    public IQueryable<Ticket> Tickets => db.Tickets;
    public IQueryable<TicketComment> Comments => db.Comments;
    public IQueryable<TicketStatusHistory> StatusHistories => db.StatusHistories;
    public IQueryable<TicketAssignmentHistory> AssignmentHistories => db.AssignmentHistories;
    public IQueryable<TicketFeedback> Feedbacks => db.Feedbacks;

    public void Add<T>(T entity) where T : class => db.Add(entity);
    public Task<int> SaveAsync() => db.SaveChangesAsync();
}
