namespace HelpdeskApi.Services;

/// <summary>Quy tắc nghiệp vụ ticket: phân quyền xem, workflow, phân công, danh sách.</summary>
public class TicketService(AppRepository repo, TicketPublisher bus, PriorityClient priority, ILogger<TicketService> log)
{
    private static readonly (TicketStatus From, TicketStatus To, bool ByCreator)[] Rules =
    [
        (TicketStatus.Open,       TicketStatus.InProgress, false),
        (TicketStatus.InProgress, TicketStatus.Pending,    false),
        (TicketStatus.Pending,    TicketStatus.InProgress, false),
        (TicketStatus.InProgress, TicketStatus.Resolved,   false),
        (TicketStatus.Resolved,   TicketStatus.Closed,     true),
        (TicketStatus.Resolved,   TicketStatus.InProgress, true),
        (TicketStatus.Open,       TicketStatus.Cancelled,  true),
    ];

    public IQueryable<Ticket> Scope(int userId, Role role) => role switch
    {
        Role.Admin => repo.Tickets,
        Role.Staff => repo.Tickets.Where(t => t.AssignedToId == userId),
        _ => repo.Tickets.Where(t => t.CreatedById == userId)
    };

    public async Task<PagedResult<TicketDto>> ListAsync(int userId, Role role, string? search, TicketStatus? status,
        int? categoryId, TicketPriority? priorityFilter, string? sort, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var q = Scope(userId, role);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(t => t.Title.Contains(search) || t.Code.Contains(search));
        if (status.HasValue) q = q.Where(t => t.Status == status);
        if (categoryId.HasValue) q = q.Where(t => t.CategoryId == categoryId);
        if (priorityFilter.HasValue) q = q.Where(t => t.Priority == priorityFilter);
        q = sort?.ToLower() switch
        {
            "priority" => q.OrderByDescending(t => t.Priority),
            "status" => q.OrderBy(t => t.Status),
            _ => q.OrderByDescending(t => t.CreatedAt)
        };
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).Select(TicketDto.Projection).ToListAsync();
        return new PagedResult<TicketDto>(page, pageSize, total, items);
    }

    public async Task<TicketDto> GetDtoAsync(int id, int userId, Role role)
    {
        await GetAccessibleAsync(id, userId, role);
        return await repo.Tickets.Where(t => t.Id == id).Select(TicketDto.Projection).FirstAsync();
    }

    public async Task<Ticket> GetAccessibleAsync(int id, int userId, Role role)
    {
        var t = await repo.Tickets.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Ticket không tồn tại");
        var ok = role == Role.Admin || t.CreatedById == userId || t.AssignedToId == userId;
        if (!ok) throw new AppException(403, "Bạn không có quyền với ticket này");
        return t;
    }

    public async Task<TicketDto> CreateAsync(int userId, CreateTicketDto dto)
    {
        var cat = await repo.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId && c.IsActive)
            ?? throw new AppException(400, "Danh mục không hợp lệ");
        var suggested = await priority.SuggestAsync(cat.Name);
        var t = new Ticket
        {
            Code = "TMP-" + Guid.NewGuid().ToString("N")[..12],
            Title = dto.Title.Trim(),
            Description = dto.Description.Trim(),
            CategoryId = dto.CategoryId,
            Priority = suggested?.Priority ?? dto.Priority,
            DueDate = suggested is null ? null : DateTime.UtcNow.AddHours(suggested.Value.SlaHours),
            CreatedById = userId,
            Status = TicketStatus.Open
        };
        repo.Add(t);
        await repo.SaveAsync();
        t.Code = $"TCK-{t.Id:D4}";
        await repo.SaveAsync();
        log.LogInformation("Created {Code} priority {Priority}", t.Code, t.Priority);
        return await repo.Tickets.Where(x => x.Id == t.Id).Select(TicketDto.Projection).FirstAsync();
    }

    public async Task<TicketDto> UpdateAsync(int id, int userId, UpdateTicketDto dto)
    {
        var t = await repo.Tickets.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Ticket không tồn tại");
        if (t.CreatedById != userId) throw new AppException(403, "Chỉ người tạo được sửa ticket");
        if (t.Status != TicketStatus.Open) throw new AppException(400, "Chỉ sửa được ticket đang Open");
        if (!await repo.Categories.AnyAsync(c => c.Id == dto.CategoryId && c.IsActive))
            throw new AppException(400, "Danh mục không hợp lệ");
        t.Title = dto.Title.Trim();
        t.Description = dto.Description.Trim();
        t.CategoryId = dto.CategoryId;
        t.Priority = dto.Priority;
        t.UpdatedAt = DateTime.UtcNow;
        await repo.SaveAsync();
        return await repo.Tickets.Where(x => x.Id == id).Select(TicketDto.Projection).FirstAsync();
    }

    public async Task<TicketDto> ChangeStatusAsync(int id, int userId, Role role, TicketStatus to, string? note)
    {
        var t = await GetAccessibleAsync(id, userId, role);
        var rule = Rules.FirstOrDefault(r => r.From == t.Status && r.To == to);
        if (rule == default) throw new AppException(400, $"Không thể chuyển từ {t.Status} sang {to}");
        if (rule.ByCreator && t.CreatedById != userId)
            throw new AppException(403, "Chỉ người tạo ticket được thực hiện thao tác này");
        if (!rule.ByCreator && t.AssignedToId != userId)
            throw new AppException(403, "Chỉ Staff được phân công mới được thực hiện thao tác này");
        if (to == TicketStatus.Resolved && string.IsNullOrWhiteSpace(note))
            throw new AppException(400, "Cần nhập ghi chú xử lý khi chuyển sang Resolved");

        repo.Add(new TicketStatusHistory
        {
            TicketId = t.Id, ChangedById = userId,
            OldStatus = t.Status, NewStatus = to, Note = note?.Trim()
        });
        t.Status = to;
        t.UpdatedAt = DateTime.UtcNow;
        if (to == TicketStatus.Resolved) t.ResolvedAt = DateTime.UtcNow;
        if (to == TicketStatus.Closed) t.ClosedAt = DateTime.UtcNow;
        if (to == TicketStatus.InProgress) t.ResolvedAt = null;
        await repo.SaveAsync();
        log.LogInformation("Ticket {Code} status {Status}", t.Code, to);
        await bus.PublishStatusAsync(new TicketStatusEvent(t.Id, t.Code, to.ToString(), t.CreatedById, t.AssignedToId));
        return await repo.Tickets.Where(x => x.Id == id).Select(TicketDto.Projection).FirstAsync();
    }

    public async Task<TicketDto> AssignAsync(int id, int adminId, int staffId, DateTime? dueDate)
    {
        var t = await repo.Tickets.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Ticket không tồn tại");
        if (t.Status is TicketStatus.Closed or TicketStatus.Cancelled)
            throw new AppException(400, "Không thể phân công ticket đã Closed/Cancelled");
        var staff = await repo.Users.FirstOrDefaultAsync(u => u.Id == staffId);
        if (staff is null || staff.Role != Role.Staff || !staff.IsActive)
            throw new AppException(400, "Người nhận phải là Staff đang hoạt động");
        if (t.AssignedToId == staffId)
            throw new AppException(400, "Ticket đã được giao cho nhân viên này");
        repo.Add(new TicketAssignmentHistory
        {
            TicketId = t.Id, AssignedById = adminId,
            OldAssigneeId = t.AssignedToId, NewAssigneeId = staffId
        });
        t.AssignedToId = staffId;
        if (dueDate.HasValue) t.DueDate = dueDate;
        t.UpdatedAt = DateTime.UtcNow;
        await repo.SaveAsync();
        return await repo.Tickets.Where(x => x.Id == id).Select(TicketDto.Projection).FirstAsync();
    }

    public async Task<TicketDto> SetPriorityAsync(int id, TicketPriority p)
    {
        var t = await repo.Tickets.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Ticket không tồn tại");
        t.Priority = p;
        t.UpdatedAt = DateTime.UtcNow;
        await repo.SaveAsync();
        return await repo.Tickets.Where(x => x.Id == id).Select(TicketDto.Projection).FirstAsync();
    }

    public async Task<List<StatusHistoryDto>> HistoryAsync(int id, int userId, Role role)
    {
        await GetAccessibleAsync(id, userId, role);
        return await repo.StatusHistories.Where(h => h.TicketId == id).OrderBy(h => h.ChangedAt)
            .Select(h => new StatusHistoryDto(h.ChangedBy!.FullName, h.OldStatus, h.NewStatus, h.Note, h.ChangedAt))
            .ToListAsync();
    }

    public async Task<List<AssignmentHistoryDto>> AssignmentsAsync(int id, int userId, Role role)
    {
        await GetAccessibleAsync(id, userId, role);
        return await repo.AssignmentHistories.Where(h => h.TicketId == id).OrderBy(h => h.AssignedAt)
            .Select(h => new AssignmentHistoryDto(h.AssignedBy!.FullName, h.OldAssigneeId, h.NewAssigneeId, h.AssignedAt))
            .ToListAsync();
    }

    public async Task<List<CommentDto>> CommentsAsync(int id, int userId, Role role)
    {
        await GetAccessibleAsync(id, userId, role);
        return await repo.Comments.Where(c => c.TicketId == id).OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(c.Id, c.UserId, c.User!.FullName, c.Content, c.CreatedAt))
            .ToListAsync();
    }

    public async Task<CommentDto> AddCommentAsync(int id, int userId, Role role, string? content)
    {
        var t = await GetAccessibleAsync(id, userId, role);
        if (t.Status is TicketStatus.Closed or TicketStatus.Cancelled)
            throw new AppException(400, "Ticket đã kết thúc, không thể bình luận");
        if (string.IsNullOrWhiteSpace(content)) throw new AppException(400, "Nội dung bình luận không được rỗng");
        var c = new TicketComment { TicketId = id, UserId = userId, Content = content.Trim() };
        repo.Add(c);
        await repo.SaveAsync();
        var name = await repo.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstAsync();
        return new CommentDto(c.Id, c.UserId, name, c.Content, c.CreatedAt);
    }

    public async Task<FeedbackDto> AddFeedbackAsync(int id, int userId, FeedbackRequest dto)
    {
        var t = await GetAccessibleAsync(id, userId, Role.User);
        if (t.CreatedById != userId) throw new AppException(403, "Chỉ người tạo ticket được đánh giá");
        if (t.Status != TicketStatus.Closed) throw new AppException(400, "Chỉ đánh giá khi ticket đã Closed");
        if (await repo.Feedbacks.AnyAsync(f => f.TicketId == id)) throw new AppException(409, "Ticket đã có đánh giá");
        var f = new TicketFeedback { TicketId = id, UserId = userId, Rating = dto.Rating, Comment = dto.Comment };
        repo.Add(f);
        await repo.SaveAsync();
        return new FeedbackDto(f.Rating, f.Comment, f.CreatedAt);
    }

    public async Task<FeedbackDto?> FeedbackAsync(int id, int userId, Role role)
    {
        await GetAccessibleAsync(id, userId, role);
        return await repo.Feedbacks.Where(x => x.TicketId == id)
            .Select(x => new FeedbackDto(x.Rating, x.Comment, x.CreatedAt)).FirstOrDefaultAsync();
    }

    public async Task<Dictionary<string, object?>> DashboardAsync(int userId, Role role)
    {
        var q = Scope(userId, role);
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
            result["averageRating"] = await repo.Feedbacks.AverageAsync(f => (double?)f.Rating);
        }
        return result;
    }
}
