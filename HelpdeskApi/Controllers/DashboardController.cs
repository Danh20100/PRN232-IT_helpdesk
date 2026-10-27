namespace HelpdeskApi.Controllers;

[ApiController, Route("api/dashboard"), Authorize]
public class DashboardController(AppDbContext db, TicketService svc) : ControllerBase
{
    // Một endpoint cho cả 3 role: số liệu tự giới hạn theo quyền (Admin: tất cả, Staff: được giao, User: của mình)
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var role = User.GetRole();
        var q = svc.Scope(User.GetId(), role);
        var now = DateTime.UtcNow;

        var byStatus = (await q.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key.ToString(), x => x.Count);

        var result = new Dictionary<string, object?>
        {
            ["total"] = await q.CountAsync(),
            ["byStatus"] = byStatus,
            ["overdue"] = await q.CountAsync(t => t.DueDate != null && t.DueDate < now
                && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed && t.Status != TicketStatus.Cancelled)
        };

        if (role == Role.Admin)
        {
            result["byCategory"] = await q.GroupBy(t => t.Category!.Name)
                .Select(g => new { Category = g.Key, Count = g.Count() }).ToListAsync();
            result["byStaff"] = await q.Where(t => t.AssignedToId != null).GroupBy(t => t.AssignedTo!.FullName)
                .Select(g => new
                {
                    Staff = g.Key,
                    Total = g.Count(),
                    Done = g.Count(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed)
                }).ToListAsync();
            result["averageRating"] = await db.Feedbacks.AverageAsync(f => (double?)f.Rating);
        }

        return Ok(result);
    }
}
