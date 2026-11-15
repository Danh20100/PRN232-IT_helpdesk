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
        var queue = string.Equals(sort, "queue", StringComparison.OrdinalIgnoreCase);
        if (queue && !status.HasValue) q = q.Where(t => t.Status != TicketStatus.Resolved);
        q = sort?.ToLower() switch
        {
            "priority" => q.OrderByDescending(t => t.Priority),
            "status" => q.OrderBy(t => t.Status),
            "queue" => q.OrderBy(t => t.Status == TicketStatus.Closed ? 2 : t.Status == TicketStatus.Cancelled ? 1 : 0)
                .ThenByDescending(t => t.Priority)
                .ThenByDescending(t => t.CreatedAt),
            _ => q.OrderBy(t => t.Status == TicketStatus.Closed || t.Status == TicketStatus.Cancelled ? 1 : 0)
                .ThenByDescending(t => t.Priority)
                .ThenByDescending(t => t.CreatedAt)
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

    public async Task<TicketDto> CreateAsync(int userId, Role role, CreateTicketDto dto)
    {
        if (dto.StaffId is int staffId)
        {
            if (role != Role.Admin) throw new AppException(403, "Chỉ Admin được phân công nhân viên");
            await EnsureActiveStaffAsync(staffId);
        }

        var cat = await repo.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId && c.IsActive)
            ?? throw new AppException(400, "Danh mục không hợp lệ");
        var suggested = await priority.SuggestAsync(cat.Name);
        var t = new Ticket
        {
            Code = "TMP-" + Guid.NewGuid().ToString("N")[..12],
            Title = dto.Title.Trim(),
            Description = dto.Description.Trim(),
            CategoryId = dto.CategoryId,
            Priority = dto.Priority ?? suggested?.Priority ?? TicketPriority.Medium,
            DueDate = suggested is null ? null : DateTime.UtcNow.AddHours(suggested.Value.SlaHours),
            CreatedById = userId,
            AssignedToId = dto.StaffId,
            Status = TicketStatus.Open
        };
        await using var transaction = await repo.BeginTransactionAsync();
        repo.Add(t);
        if (dto.StaffId is int assigneeId)
        {
            repo.Add(new TicketAssignmentHistory
            {
                Ticket = t, AssignedById = userId, NewAssigneeId = assigneeId
            });
        }
        await repo.SaveAsync();
        t.Code = $"TCK-{t.Id:D4}";
        await repo.SaveAsync();
        await transaction.CommitAsync();
        log.LogInformation("Created {Code} priority {Priority}", t.Code, t.Priority);

        // User tạo Ticket mới → thông báo Admin
        var creatorName = await repo.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync();
        var adminIds = await GetActiveAdminIdsAsync();
        await NotifyAsync(new TicketStatusEvent(
            t.Id, t.Code, t.Status.ToString(), t.CreatedById, t.AssignedToId,
            ActorId: userId, TicketTitle: t.Title, EventType: "Created",
            ActorName: creatorName, AdminIds: adminIds));

        // Nếu tạo kèm phân công Staff → thông báo Staff được giao
        if (dto.StaffId is int assignedStaffId)
        {
            var staffName = await repo.Users.Where(u => u.Id == assignedStaffId).Select(u => u.FullName).FirstOrDefaultAsync();
            await NotifyAsync(new TicketStatusEvent(
                t.Id, t.Code, t.Status.ToString(), t.CreatedById, t.AssignedToId,
                ActorId: userId, TicketTitle: t.Title, EventType: "Assigned",
                ActorName: staffName, OldAssignedToId: null));
        }

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
        var adminClose = role == Role.Admin && t.Status == TicketStatus.Resolved && to == TicketStatus.Closed;
        if (rule.ByCreator && t.CreatedById != userId && !adminClose)
            throw new AppException(403, "Chỉ người tạo ticket được thực hiện thao tác này");
        if (!rule.ByCreator && t.AssignedToId != userId)
            throw new AppException(403, "Chỉ Staff được phân công mới được thực hiện thao tác này");
        if (to == TicketStatus.Resolved && string.IsNullOrWhiteSpace(note))
            throw new AppException(400, "Cần nhập ghi chú xử lý khi chuyển sang Resolved");

        var oldStatus = t.Status;
        var cleanNote = note?.Trim();
        repo.Add(new TicketStatusHistory
        {
            TicketId = t.Id, ChangedById = userId,
            OldStatus = oldStatus, NewStatus = to, Note = cleanNote
        });
        t.Status = to;
        t.UpdatedAt = DateTime.UtcNow;
        if (to == TicketStatus.Resolved) t.ResolvedAt = DateTime.UtcNow;
        if (to == TicketStatus.Closed) t.ClosedAt = DateTime.UtcNow;
        if (to == TicketStatus.InProgress) t.ResolvedAt = null;
        await repo.SaveAsync();
        log.LogInformation("Ticket {Code} status {Status}", t.Code, to);

        var needAdmins = (oldStatus == TicketStatus.Resolved && to == TicketStatus.InProgress)
                         || to == TicketStatus.Cancelled;
        var adminIds = needAdmins ? await GetActiveAdminIdsAsync() : null;

        await NotifyAsync(new TicketStatusEvent(
            t.Id, t.Code, to.ToString(), t.CreatedById, t.AssignedToId,
            ActorId: userId, OldStatus: oldStatus.ToString(), TicketTitle: t.Title,
            Note: cleanNote, EventType: "StatusChanged", AdminIds: adminIds));
        return await repo.Tickets.Where(x => x.Id == id).Select(TicketDto.Projection).FirstAsync();
    }

    private async Task EnsureActiveStaffAsync(int staffId)
    {
        if (!await repo.Users.AnyAsync(u => u.Id == staffId && u.Role == Role.Staff && u.IsActive))
            throw new AppException(400, "Người nhận phải là Staff đang hoạt động");
    }

    private Task<List<int>> GetActiveAdminIdsAsync() =>
        repo.Users.Where(u => u.Role == Role.Admin && u.IsActive).Select(u => u.Id).ToListAsync();

    public async Task<TicketDto> AssignAsync(int id, int adminId, int staffId, DateTime? dueDate)
    {
        var t = await repo.Tickets.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Ticket không tồn tại");
        if (t.Status is TicketStatus.Closed or TicketStatus.Cancelled)
            throw new AppException(400, "Không thể phân công ticket đã Closed/Cancelled");
        await EnsureActiveStaffAsync(staffId);
        if (t.AssignedToId == staffId)
            throw new AppException(400, "Ticket đã được giao cho nhân viên này");
        var oldAssigneeId = t.AssignedToId;
        repo.Add(new TicketAssignmentHistory
        {
            TicketId = t.Id, AssignedById = adminId,
            OldAssigneeId = oldAssigneeId, NewAssigneeId = staffId
        });
        t.AssignedToId = staffId;
        if (dueDate.HasValue) t.DueDate = dueDate;
        t.UpdatedAt = DateTime.UtcNow;
        await repo.SaveAsync();
        var staffName = await repo.Users.Where(u => u.Id == staffId).Select(u => u.FullName).FirstOrDefaultAsync();
        var eventType = oldAssigneeId.HasValue ? "Reassigned" : "Assigned";
        await NotifyAsync(new TicketStatusEvent(
            t.Id, t.Code, t.Status.ToString(), t.CreatedById, t.AssignedToId,
            ActorId: adminId, TicketTitle: t.Title, EventType: eventType,
            ActorName: staffName, OldAssignedToId: oldAssigneeId));
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
        await NotifyAsync(new TicketStatusEvent(
            t.Id, t.Code, t.Status.ToString(), t.CreatedById, t.AssignedToId,
            ActorId: userId, TicketTitle: t.Title, Note: c.Content, EventType: "CommentAdded", ActorName: name));
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

    public async Task<Dictionary<string, object?>> DashboardAsync(int userId, Role role, DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue && from > to) (from, to) = (to, from);
        // Ngày lịch Việt Nam: 00:00 UTC+7 đến trước 00:00 ngày hôm sau.
        DateTime? start = from?.ToDateTime(TimeOnly.MinValue).AddHours(-7);
        DateTime? end = to?.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var q = Scope(userId, role);
        if (start.HasValue) q = q.Where(t => t.CreatedAt >= start.Value);
        if (end.HasValue) q = q.Where(t => t.CreatedAt < end.Value);
        var now = DateTime.UtcNow;
        var byStatus = (await q.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key.ToString(), x => x.Count);
        var byPriority = (await q.GroupBy(t => t.Priority).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key.ToString(), x => x.Count);
        // ponytail: bucket by Vietnam local day (UTC+7)
        var created = await q.Select(t => t.CreatedAt).ToListAsync();
        var series = created
            .GroupBy(d => d.AddHours(7).Date)
            .OrderBy(g => g.Key)
            .Select(g => new { Date = g.Key.ToString("yyyy-MM-dd"), Count = g.Count() })
            .ToList();
        var result = new Dictionary<string, object?>
        {
            ["total"] = await q.CountAsync(),
            ["byStatus"] = byStatus,
            ["byPriority"] = byPriority,
            ["series"] = series,
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
            var feedback = repo.Feedbacks.AsQueryable();
            if (start.HasValue) feedback = feedback.Where(f => f.CreatedAt >= start.Value);
            if (end.HasValue) feedback = feedback.Where(f => f.CreatedAt < end.Value);
            result["averageRating"] = await feedback.AverageAsync(f => (double?)f.Rating);
        }
        return result;
    }

    // ---- Notifications ----
    private async Task NotifyAsync(TicketStatusEvent e)
    {
        var handledByWorker = await bus.PublishStatusAsync(e);
        if (handledByWorker) return;

        // Fallback an toàn khi chạy dev local không bật HelpdeskWorker hoặc Redis.
        try
        {
            var items = TicketPublisher.BuildNotifications(e);
            if (items.Count == 0) return;

            var cutoff = DateTime.UtcNow.AddSeconds(-10);
            var added = false;
            foreach (var (recipientId, title, message) in items)
            {
                var exists = await repo.Notifications.AnyAsync(n =>
                    n.UserId == recipientId &&
                    n.TicketId == e.TicketId &&
                    n.Title == title &&
                    n.Message == message &&
                    n.CreatedAt >= cutoff);
                if (exists) continue;

                repo.Add(new Notification
                {
                    UserId = recipientId,
                    TicketId = e.TicketId,
                    Title = title,
                    Message = message,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                added = true;
            }
            if (added) await repo.SaveAsync();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallback notification save failed for {Code}", e.Code);
        }
    }

    private async Task EnsureOverdueNotificationsAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var activeTickets = await repo.Tickets
                .Where(t => t.Status == TicketStatus.Open
                    || t.Status == TicketStatus.InProgress
                    || t.Status == TicketStatus.Pending)
                .Select(t => new { t.Id, t.Code, t.Title, t.CreatedById, t.AssignedToId, t.CreatedAt })
                .ToListAsync();

            if (activeTickets.Count == 0) return;

            var ticketIds = activeTickets.Select(t => t.Id).ToList();
            var reopenMap = await repo.StatusHistories
                .Where(h => ticketIds.Contains(h.TicketId)
                    && h.OldStatus == TicketStatus.Resolved
                    && h.NewStatus == TicketStatus.InProgress)
                .GroupBy(h => h.TicketId)
                .Select(g => new { TicketId = g.Key, LastReopenedAt = g.Max(x => x.ChangedAt) })
                .ToDictionaryAsync(x => x.TicketId, x => x.LastReopenedAt);

            var adminIds = await GetActiveAdminIdsAsync();
            var added = false;

            foreach (var t in activeTickets)
            {
                var slaStartAt = reopenMap.TryGetValue(t.Id, out var reopenedAt) && reopenedAt > t.CreatedAt
                    ? reopenedAt
                    : t.CreatedAt;

                var elapsedHours = (now - slaStartAt).TotalHours;
                if (elapsedHours < 20.0) continue;

                // 1. Mốc 20h Warning (gửi bù cả khi Worker bị dừng qua mốc 24h)
                if (elapsedHours >= 20.0 && t.AssignedToId is int staffId)
                {
                    const string warnTitle = "Ticket sắp quá hạn SLA";
                    var warnMsg = $"Ticket {t.Code} đã được xử lý trong 20 giờ và chỉ còn 4 giờ trước khi vượt quá SLA 24 giờ.";
                    if (await TryInsertSlaNotificationAsync(staffId, t.Id, warnTitle, warnMsg, slaStartAt, now))
                        added = true;
                }

                // 2. Mốc 24h Overdue
                if (elapsedHours >= 24.0)
                {
                    const string overdueTitle = "Ticket đã quá hạn SLA";
                    var overdueMsg = $"Ticket {t.Code} đã vượt quá thời gian xử lý 24 giờ. Vui lòng kiểm tra và cập nhật tiến độ xử lý.";
                    var recipients = new List<int>(adminIds);
                    if (t.AssignedToId is int sId && !recipients.Contains(sId)) recipients.Add(sId);

                    foreach (var uid in recipients.Distinct())
                    {
                        if (await TryInsertSlaNotificationAsync(uid, t.Id, overdueTitle, overdueMsg, slaStartAt, now))
                            added = true;
                    }
                }

                // 3. Mốc 48h Overdue Reminder
                if (elapsedHours >= 48.0)
                {
                    const string reminderTitle = "Cảnh báo nhắc lại: Ticket quá hạn SLA 48 giờ";
                    var reminderMsg = $"Ticket {t.Code} đã vượt quá 48 giờ xử lý mà chưa được giải quyết. Vui lòng kiểm tra và cập nhật tiến độ xử lý.";
                    var recipients = new List<int>(adminIds);
                    if (t.AssignedToId is int sId && !recipients.Contains(sId)) recipients.Add(sId);

                    foreach (var uid in recipients.Distinct())
                    {
                        if (await TryInsertSlaNotificationAsync(uid, t.Id, reminderTitle, reminderMsg, slaStartAt, now))
                            added = true;
                    }
                }
            }

            if (added) await repo.SaveAsync();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "SLA notification check failed");
        }
    }

    private async Task<bool> TryInsertSlaNotificationAsync(
        int userId, int ticketId, string title, string message, DateTime slaStartAt, DateTime now)
    {
        var exists = await repo.Notifications.AnyAsync(n =>
            n.UserId == userId &&
            n.TicketId == ticketId &&
            n.Title == title &&
            n.CreatedAt >= slaStartAt);
        if (exists) return false;

        repo.Add(new Notification
        {
            UserId = userId,
            TicketId = ticketId,
            Title = title,
            Message = message,
            IsRead = false,
            CreatedAt = now
        });
        return true;
    }

    // ---- SLA Monitoring & Time Tracking Dashboard ----
    private static SlaTicketItemDto ComputeSlaItem(
        TicketDto t,
        DateTime? lastReopenedAt,
        DateTime? lastResolvedAtFromHistory,
        DateTime nowUtc)
    {
        var isReopened = lastReopenedAt.HasValue && lastReopenedAt.Value > t.CreatedAt;
        var slaStartAt = isReopened ? lastReopenedAt!.Value : t.CreatedAt;
        var slaDeadline = slaStartAt.AddHours(24);
        var isCompleted = t.Status is TicketStatus.Resolved or TicketStatus.Closed or TicketStatus.Cancelled;

        DateTime endTime;
        if (isCompleted)
        {
            // Lấy đúng thời điểm kết thúc của chu kỳ hiện tại (>= slaStartAt), không cộng dồn chu kỳ trước
            DateTime? candidate = t.ResolvedAt ?? lastResolvedAtFromHistory ?? t.ClosedAt;
            if (candidate.HasValue && candidate.Value >= slaStartAt)
                endTime = candidate.Value;
            else if (t.ClosedAt.HasValue && t.ClosedAt.Value >= slaStartAt)
                endTime = t.ClosedAt.Value;
            else
                endTime = candidate ?? nowUtc;
        }
        else
        {
            endTime = nowUtc;
        }

        var elapsed = endTime > slaStartAt ? endTime - slaStartAt : TimeSpan.Zero;
        var elapsedHours = Math.Round(elapsed.TotalHours, 2);
        var elapsedMinutes = Math.Max(0, (int)Math.Floor(elapsed.TotalMinutes));

        int remainingMinutes;
        if (isCompleted)
        {
            remainingMinutes = (int)Math.Round((slaDeadline - endTime).TotalMinutes);
        }
        else
        {
            remainingMinutes = (int)Math.Ceiling((slaDeadline - nowUtc).TotalMinutes);
        }

        string slaStatus;
        if (isCompleted)
        {
            slaStatus = "COMPLETED";
        }
        else if (elapsed.TotalHours >= 24.0)
        {
            slaStatus = "OVERDUE";
        }
        else if (elapsed.TotalHours >= 20.0)
        {
            slaStatus = "WARNING";
        }
        else
        {
            slaStatus = "NORMAL";
        }

        var progressPercent = Math.Round(Math.Clamp((elapsed.TotalHours / 24.0) * 100.0, 0.0, 100.0), 1);

        return new SlaTicketItemDto(
            t.Id,
            t.Code,
            t.Title,
            t.Category,
            t.Priority,
            t.Status,
            t.CreatedById,
            t.CreatedBy,
            t.AssignedToId,
            t.AssignedTo,
            t.CreatedAt,
            slaStartAt,
            slaDeadline,
            t.DueDate,
            t.ResolvedAt ?? lastResolvedAtFromHistory,
            elapsedHours,
            elapsedMinutes,
            remainingMinutes,
            progressPercent,
            slaStatus,
            isReopened);
    }

    private async Task<List<SlaTicketItemDto>> LoadScopedSlaItemsAsync(
        int userId, Role role, DateTime? start = null, DateTime? end = null)
    {
        var q = Scope(userId, role);
        if (start.HasValue) q = q.Where(t => t.CreatedAt >= start.Value);
        if (end.HasValue) q = q.Where(t => t.CreatedAt < end.Value);

        var tickets = await q.Select(TicketDto.Projection).ToListAsync();
        if (tickets.Count == 0) return [];

        var ids = tickets.Select(t => t.Id).ToList();
        var histories = await repo.StatusHistories
            .Where(h => ids.Contains(h.TicketId) &&
                ((h.OldStatus == TicketStatus.Resolved && h.NewStatus == TicketStatus.InProgress) ||
                 h.NewStatus == TicketStatus.Resolved))
            .Select(h => new { h.TicketId, h.OldStatus, h.NewStatus, h.ChangedAt })
            .ToListAsync();

        var reopenMap = histories
            .Where(h => h.OldStatus == TicketStatus.Resolved && h.NewStatus == TicketStatus.InProgress)
            .GroupBy(h => h.TicketId)
            .ToDictionary(g => g.Key, g => (DateTime?)g.Max(x => x.ChangedAt));

        var resolvedMap = histories
            .Where(h => h.NewStatus == TicketStatus.Resolved)
            .GroupBy(h => h.TicketId)
            .ToDictionary(g => g.Key, g => (DateTime?)g.Max(x => x.ChangedAt));

        var now = DateTime.UtcNow;
        return tickets
            .Select(t => ComputeSlaItem(
                t,
                reopenMap.GetValueOrDefault(t.Id),
                resolvedMap.GetValueOrDefault(t.Id),
                now))
            .ToList();
    }

    public async Task<SlaOverviewDto> GetSlaOverviewAsync(int userId, Role role, DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue && from > to) (from, to) = (to, from);
        DateTime? start = from?.ToDateTime(TimeOnly.MinValue).AddHours(-7);
        DateTime? end = to?.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7);

        var items = await LoadScopedSlaItemsAsync(userId, role, start, end);

        var onTrack = items.Count(x => x.SlaStatus == "NORMAL");
        var nearDeadline = items.Count(x => x.SlaStatus == "WARNING");
        var overdue = items.Count(x => x.SlaStatus == "OVERDUE");
        var completed = items.Count(x => x.SlaStatus == "COMPLETED");
        var totalActive = onTrack + nearDeadline + overdue;

        // AverageResolutionHours: chỉ tính trên các ticket đã hoàn thành xử lý (Resolved hoặc Closed) trong chu kỳ hiện tại
        var resolvedItems = items
            .Where(x => (x.Status == TicketStatus.Resolved || x.Status == TicketStatus.Closed) && x.ResolvedAt.HasValue && x.ResolvedAt.Value >= x.SlaStartAt)
            .ToList();
        var avgHours = resolvedItems.Count > 0
            ? Math.Round(resolvedItems.Average(x => (x.ResolvedAt!.Value - x.SlaStartAt).TotalHours), 2)
            : 0.0;

        var bySlaStatus = new List<SlaStatusCountDto>
        {
            new("NORMAL", onTrack),
            new("WARNING", nearDeadline),
            new("OVERDUE", overdue),
            new("COMPLETED", completed)
        };

        var completedTrend = items
            .Where(x => x.SlaStatus == "COMPLETED")
            .Select(x => (x.ResolvedAt ?? x.CreatedAt).AddHours(7).Date)
            .GroupBy(d => d)
            .OrderBy(g => g.Key)
            .Select(g => new SlaTrendPointDto(g.Key.ToString("yyyy-MM-dd"), g.Count()))
            .ToList();

        return new SlaOverviewDto(
            totalActive,
            onTrack,
            nearDeadline,
            overdue,
            completed,
            avgHours,
            bySlaStatus,
            completedTrend);
    }

    public async Task<PagedResult<SlaTicketItemDto>> GetSlaTicketsAsync(
        int userId,
        Role role,
        string? search,
        TicketPriority? priorityFilter,
        TicketStatus? statusFilter,
        int? staffId,
        string? slaStatus,
        string? sort,
        int page,
        int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var items = await LoadScopedSlaItemsAsync(userId, role);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var kw = search.Trim();
            items = items.Where(x =>
                x.Code.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                x.Title.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (priorityFilter.HasValue)
            items = items.Where(x => x.Priority == priorityFilter.Value).ToList();
        if (statusFilter.HasValue)
            items = items.Where(x => x.Status == statusFilter.Value).ToList();
        if (role == Role.Admin && staffId.HasValue)
            items = items.Where(x => x.AssignedToId == staffId.Value).ToList();
        if (!string.IsNullOrWhiteSpace(slaStatus))
            items = items.Where(x => string.Equals(x.SlaStatus, slaStatus.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        items = (sort?.ToLowerInvariant()) switch
        {
            "overdue_desc" => items
                .OrderBy(x => x.SlaStatus == "OVERDUE" ? 0 : x.SlaStatus == "WARNING" ? 1 : x.SlaStatus == "NORMAL" ? 2 : 3)
                .ThenBy(x => x.RemainingMinutes)
                .ThenByDescending(x => x.ElapsedMinutes)
                .ToList(),
            "remaining_asc" => items
                .OrderBy(x => x.SlaStatus == "COMPLETED" ? 1 : 0)
                .ThenBy(x => x.RemainingMinutes)
                .ToList(),
            "elapsed_desc" => items
                .OrderByDescending(x => x.ElapsedMinutes)
                .ToList(),
            "created_desc" => items
                .OrderByDescending(x => x.CreatedAt)
                .ToList(),
            _ => items
                .OrderBy(x => x.SlaStatus == "OVERDUE" ? 0 : x.SlaStatus == "WARNING" ? 1 : x.SlaStatus == "NORMAL" ? 2 : 3)
                .ThenBy(x => x.RemainingMinutes)
                .ThenByDescending(x => x.Priority)
                .ToList()
        };

        var total = items.Count;
        var paged = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<SlaTicketItemDto>(page, pageSize, total, paged);
    }

    public async Task<List<StaffSlaPerformanceDto>> GetStaffPerformanceAsync(int userId, Role role)
    {
        var staffQuery = repo.Users.Where(u => u.Role == Role.Staff && u.IsActive);
        if (role == Role.Staff)
            staffQuery = staffQuery.Where(u => u.Id == userId);

        var staffList = await staffQuery
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Email })
            .ToListAsync();

        var items = await LoadScopedSlaItemsAsync(userId, role);

        return staffList.Select(s =>
        {
            var mine = items.Where(t => t.AssignedToId == s.Id).ToList();
            var onTrack = mine.Count(t => t.SlaStatus == "NORMAL");
            var warning = mine.Count(t => t.SlaStatus == "WARNING");
            var overdue = mine.Count(t => t.SlaStatus == "OVERDUE");
            var completed = mine.Count(t => t.SlaStatus == "COMPLETED");
            var active = onTrack + warning + overdue;

            var resolvedMine = mine
                .Where(t => (t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed)
                            && t.ResolvedAt.HasValue && t.ResolvedAt.Value >= t.SlaStartAt)
                .ToList();
            var avgHours = resolvedMine.Count > 0
                ? Math.Round(resolvedMine.Average(t => (t.ResolvedAt!.Value - t.SlaStartAt).TotalHours), 2)
                : 0.0;

            return new StaffSlaPerformanceDto(
                s.Id,
                s.FullName,
                s.Email,
                active,
                onTrack,
                warning,
                overdue,
                completed,
                avgHours);
        }).ToList();
    }

    private async Task EnsureRecentTicketCreatedNotificationsAsync()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var recentTickets = await repo.Tickets
                .Where(t => t.CreatedAt >= cutoff && t.Status == TicketStatus.Open)
                .Select(t => new
                {
                    t.Id,
                    t.Code,
                    t.Title,
                    t.CreatedById,
                    t.AssignedToId,
                    t.CreatedAt,
                    CreatorName = t.CreatedBy != null ? t.CreatedBy.FullName : "Người dùng"
                })
                .ToListAsync();

            if (recentTickets.Count == 0) return;

            var adminIds = await GetActiveAdminIdsAsync();
            if (adminIds.Count == 0) return;

            var added = false;
            foreach (var t in recentTickets)
            {
                var e = new TicketStatusEvent(
                    t.Id, t.Code, nameof(TicketStatus.Open), t.CreatedById, t.AssignedToId,
                    ActorId: t.CreatedById, TicketTitle: t.Title, EventType: "Created",
                    ActorName: t.CreatorName, AdminIds: adminIds);

                var items = TicketPublisher.BuildNotifications(e);
                foreach (var (recipientId, title, message) in items)
                {
                    var alreadyNotified = await repo.Notifications.AnyAsync(n =>
                        n.UserId == recipientId &&
                        n.TicketId == t.Id &&
                        n.Title == title);
                    if (alreadyNotified) continue;

                    repo.Add(new Notification
                    {
                        UserId = recipientId,
                        TicketId = t.Id,
                        Title = title,
                        Message = message,
                        IsRead = false,
                        CreatedAt = t.CreatedAt
                    });
                    added = true;
                }
            }

            if (added) await repo.SaveAsync();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Recent ticket created notification backfill failed");
        }
    }

    public async Task<NotificationListDto> NotificationsAsync(int userId, int take = 30)
    {
        await EnsureRecentTicketCreatedNotificationsAsync();
        await EnsureOverdueNotificationsAsync();

        take = Math.Clamp(take, 1, 100);
        var q = repo.Notifications.Where(n => n.UserId == userId);
        var unreadCount = await q.CountAsync(n => !n.IsRead);
        var items = await q
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(take)
            .Select(n => new NotificationDto(n.Id, n.TicketId, n.Title, n.Message, n.IsRead, n.CreatedAt))
            .ToListAsync();
        return new NotificationListDto(unreadCount, items);
    }

    public async Task MarkNotificationReadAsync(int id, int userId)
    {
        var n = await repo.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId)
            ?? throw new AppException(404, "Thông báo không tồn tại");
        if (!n.IsRead)
        {
            n.IsRead = true;
            await repo.SaveAsync();
        }
    }

    public async Task MarkAllNotificationsReadAsync(int userId)
    {
        var unread = await repo.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();
        if (unread.Count == 0) return;
        foreach (var n in unread) n.IsRead = true;
        await repo.SaveAsync();
    }
}
