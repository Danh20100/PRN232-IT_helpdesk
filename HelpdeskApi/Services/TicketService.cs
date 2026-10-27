namespace HelpdeskApi.Services;

/// <summary>Chứa toàn bộ quy tắc nghiệp vụ của Ticket (phân quyền xem, workflow trạng thái, phân công).</summary>
public class TicketService(AppDbContext db)
{
    // Các chuyển trạng thái hợp lệ. ByCreator = true: người tạo thực hiện; false: Staff được phân công.
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

    /// <summary>Admin: tất cả; Staff: ticket được giao; User: ticket do mình tạo.</summary>
    public IQueryable<Ticket> Scope(int userId, Role role) => role switch
    {
        Role.Admin => db.Tickets.AsQueryable(),
        Role.Staff => db.Tickets.Where(t => t.AssignedToId == userId),
        _ => db.Tickets.Where(t => t.CreatedById == userId)
    };

    public async Task<Ticket> GetAccessibleAsync(int id, int userId, Role role)
    {
        var t = await db.Tickets.FindAsync(id) ?? throw new AppException(404, "Ticket không tồn tại");
        var ok = role == Role.Admin || t.CreatedById == userId || t.AssignedToId == userId;
        if (!ok) throw new AppException(403, "Bạn không có quyền với ticket này");
        return t;
    }

    private async Task EnsureCategoryAsync(int categoryId)
    {
        var c = await db.Categories.FindAsync(categoryId);
        if (c is null || !c.IsActive) throw new AppException(400, "Danh mục không hợp lệ");
    }

    public async Task<int> CreateAsync(int userId, CreateTicketDto dto)
    {
        await EnsureCategoryAsync(dto.CategoryId);
        var t = new Ticket
        {
            Code = "TMP-" + Guid.NewGuid().ToString("N")[..12],
            Title = dto.Title.Trim(),
            Description = dto.Description.Trim(),
            CategoryId = dto.CategoryId,
            Priority = dto.Priority,
            CreatedById = userId,
            Status = TicketStatus.Open
        };
        db.Tickets.Add(t);
        await db.SaveChangesAsync();
        t.Code = $"TCK-{t.Id:D4}"; // có Id rồi mới sinh mã hiển thị
        await db.SaveChangesAsync();
        return t.Id;
    }

    public async Task UpdateAsync(int id, int userId, UpdateTicketDto dto)
    {
        var t = await db.Tickets.FindAsync(id) ?? throw new AppException(404, "Ticket không tồn tại");
        if (t.CreatedById != userId) throw new AppException(403, "Chỉ người tạo được sửa ticket");
        if (t.Status != TicketStatus.Open) throw new AppException(400, "Chỉ sửa được ticket đang Open");
        await EnsureCategoryAsync(dto.CategoryId);
        t.Title = dto.Title.Trim();
        t.Description = dto.Description.Trim();
        t.CategoryId = dto.CategoryId;
        t.Priority = dto.Priority;
        t.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task ChangeStatusAsync(int id, int userId, Role role, TicketStatus to, string? note)
    {
        var t = await GetAccessibleAsync(id, userId, role);

        var rule = Rules.FirstOrDefault(r => r.From == t.Status && r.To == to);
        if (rule == default)
            throw new AppException(400, $"Không thể chuyển từ {t.Status} sang {to}");

        if (rule.ByCreator && t.CreatedById != userId)
            throw new AppException(403, "Chỉ người tạo ticket được thực hiện thao tác này");
        if (!rule.ByCreator && t.AssignedToId != userId)
            throw new AppException(403, "Chỉ Staff được phân công mới được thực hiện thao tác này");

        if (to == TicketStatus.Resolved && string.IsNullOrWhiteSpace(note))
            throw new AppException(400, "Cần nhập ghi chú xử lý khi chuyển sang Resolved");

        db.StatusHistories.Add(new TicketStatusHistory
        {
            TicketId = t.Id, ChangedById = userId,
            OldStatus = t.Status, NewStatus = to, Note = note?.Trim()
        });

        t.Status = to;
        t.UpdatedAt = DateTime.UtcNow;
        if (to == TicketStatus.Resolved) t.ResolvedAt = DateTime.UtcNow;
        if (to == TicketStatus.Closed) t.ClosedAt = DateTime.UtcNow;
        if (to == TicketStatus.InProgress) t.ResolvedAt = null; // mở lại

        await db.SaveChangesAsync();
    }

    public async Task AssignAsync(int id, int adminId, int staffId, DateTime? dueDate)
    {
        var t = await db.Tickets.FindAsync(id) ?? throw new AppException(404, "Ticket không tồn tại");
        if (t.Status is TicketStatus.Closed or TicketStatus.Cancelled)
            throw new AppException(400, "Không thể phân công ticket đã Closed/Cancelled");

        var staff = await db.Users.FindAsync(staffId);
        if (staff is null || staff.Role != Role.Staff || !staff.IsActive)
            throw new AppException(400, "Người nhận phải là Staff đang hoạt động");
        if (t.AssignedToId == staffId)
            throw new AppException(400, "Ticket đã được giao cho nhân viên này");

        db.AssignmentHistories.Add(new TicketAssignmentHistory
        {
            TicketId = t.Id, AssignedById = adminId,
            OldAssigneeId = t.AssignedToId, NewAssigneeId = staffId
        });

        t.AssignedToId = staffId;
        if (dueDate.HasValue) t.DueDate = dueDate;
        t.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SetPriorityAsync(int id, TicketPriority priority)
    {
        var t = await db.Tickets.FindAsync(id) ?? throw new AppException(404, "Ticket không tồn tại");
        t.Priority = priority;
        t.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
