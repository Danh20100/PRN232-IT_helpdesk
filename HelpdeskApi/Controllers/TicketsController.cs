namespace HelpdeskApi.Controllers;

[ApiController, Route("api/tickets"), Authorize]
public class TicketsController(AppDbContext db, TicketService svc) : ControllerBase
{
    private Task<TicketDto> Load(int id)
        => db.Tickets.Where(t => t.Id == id).Select(TicketDto.Projection).FirstAsync();

    // ---------- CRUD ----------
    [HttpPost, Authorize(Roles = "User")]
    public async Task<IActionResult> Create(CreateTicketDto dto)
    {
        var id = await svc.CreateAsync(User.GetId(), dto);
        return CreatedAtAction(nameof(Get), new { id }, await Load(id));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] TicketStatus? status,
        [FromQuery] int? categoryId, [FromQuery] TicketPriority? priority,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var q = svc.Scope(User.GetId(), User.GetRole());
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(t => t.Title.Contains(search) || t.Code.Contains(search));
        if (status.HasValue) q = q.Where(t => t.Status == status);
        if (categoryId.HasValue) q = q.Where(t => t.CategoryId == categoryId);
        if (priority.HasValue) q = q.Where(t => t.Priority == priority);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(TicketDto.Projection).ToListAsync();
        return Ok(new PagedResult<TicketDto>(page, pageSize, total, items));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        await svc.GetAccessibleAsync(id, User.GetId(), User.GetRole());
        return Ok(await Load(id));
    }

    [HttpPut("{id:int}"), Authorize(Roles = "User")]
    public async Task<IActionResult> Update(int id, UpdateTicketDto dto)
    {
        await svc.UpdateAsync(id, User.GetId(), dto);
        return Ok(await Load(id));
    }

    // Hủy ticket khi còn Open (tương đương Open -> Cancelled)
    [HttpDelete("{id:int}"), Authorize(Roles = "User")]
    public async Task<IActionResult> Cancel(int id)
    {
        await svc.ChangeStatusAsync(id, User.GetId(), User.GetRole(), TicketStatus.Cancelled, null);
        return NoContent();
    }

    // ---------- Admin: phân công / ưu tiên ----------
    [HttpPatch("{id:int}/assign"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Assign(int id, AssignDto dto)
    {
        await svc.AssignAsync(id, User.GetId(), dto.StaffId, dto.DueDate);
        return Ok(await Load(id));
    }

    [HttpPatch("{id:int}/priority"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetPriority(int id, SetPriorityDto dto)
    {
        await svc.SetPriorityAsync(id, dto.Priority);
        return Ok(await Load(id));
    }

    // ---------- Workflow ----------
    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(int id, ChangeStatusDto dto)
    {
        await svc.ChangeStatusAsync(id, User.GetId(), User.GetRole(), dto.Status, dto.Note);
        return Ok(await Load(id));
    }

    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> History(int id)
    {
        await svc.GetAccessibleAsync(id, User.GetId(), User.GetRole());
        return Ok(await db.StatusHistories.Where(h => h.TicketId == id).OrderBy(h => h.ChangedAt)
            .Select(h => new StatusHistoryDto(h.ChangedBy!.FullName, h.OldStatus, h.NewStatus, h.Note, h.ChangedAt))
            .ToListAsync());
    }

    [HttpGet("{id:int}/assignments")]
    public async Task<IActionResult> Assignments(int id)
    {
        await svc.GetAccessibleAsync(id, User.GetId(), User.GetRole());
        return Ok(await db.AssignmentHistories.Where(h => h.TicketId == id).OrderBy(h => h.AssignedAt)
            .Select(h => new AssignmentHistoryDto(h.AssignedBy!.FullName, h.OldAssigneeId, h.NewAssigneeId, h.AssignedAt))
            .ToListAsync());
    }

    // ---------- Comments ----------
    [HttpGet("{id:int}/comments")]
    public async Task<IActionResult> Comments(int id)
    {
        await svc.GetAccessibleAsync(id, User.GetId(), User.GetRole());
        return Ok(await db.Comments.Where(c => c.TicketId == id).OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(c.Id, c.UserId, c.User!.FullName, c.Content, c.CreatedAt))
            .ToListAsync());
    }

    [HttpPost("{id:int}/comments")]
    public async Task<IActionResult> AddComment(int id, CommentRequest dto)
    {
        var t = await svc.GetAccessibleAsync(id, User.GetId(), User.GetRole());
        if (t.Status is TicketStatus.Closed or TicketStatus.Cancelled)
            throw new AppException(400, "Ticket đã kết thúc, không thể bình luận");
        if (string.IsNullOrWhiteSpace(dto.Content))
            throw new AppException(400, "Nội dung bình luận không được rỗng");

        var c = new TicketComment { TicketId = id, UserId = User.GetId(), Content = dto.Content.Trim() };
        db.Comments.Add(c);
        await db.SaveChangesAsync();
        var name = await db.Users.Where(u => u.Id == c.UserId).Select(u => u.FullName).FirstAsync();
        return Ok(new CommentDto(c.Id, c.UserId, name, c.Content, c.CreatedAt));
    }

    // ---------- Feedback ----------
    [HttpPost("{id:int}/feedback"), Authorize(Roles = "User")]
    public async Task<IActionResult> AddFeedback(int id, FeedbackRequest dto)
    {
        var t = await svc.GetAccessibleAsync(id, User.GetId(), User.GetRole());
        if (t.CreatedById != User.GetId()) throw new AppException(403, "Chỉ người tạo ticket được đánh giá");
        if (t.Status != TicketStatus.Closed) throw new AppException(400, "Chỉ đánh giá khi ticket đã Closed");
        if (await db.Feedbacks.AnyAsync(f => f.TicketId == id)) throw new AppException(409, "Ticket đã có đánh giá");

        var f = new TicketFeedback { TicketId = id, UserId = User.GetId(), Rating = dto.Rating, Comment = dto.Comment };
        db.Feedbacks.Add(f);
        await db.SaveChangesAsync();
        return Ok(new FeedbackDto(f.Rating, f.Comment, f.CreatedAt));
    }

    [HttpGet("{id:int}/feedback")]
    public async Task<IActionResult> GetFeedback(int id)
    {
        await svc.GetAccessibleAsync(id, User.GetId(), User.GetRole());
        var f = await db.Feedbacks.Where(x => x.TicketId == id)
            .Select(x => new FeedbackDto(x.Rating, x.Comment, x.CreatedAt)).FirstOrDefaultAsync();
        return f is null ? NotFound() : Ok(f);
    }
}
