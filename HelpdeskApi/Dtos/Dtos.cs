using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace HelpdeskApi.Dtos;

// ---- Auth / User ----
public record RegisterDto([Required, StringLength(100)] string FullName, [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password, string? PhoneNumber);
public record LoginDto([Required] string Email, [Required] string Password);
public record AuthResponse(string Token, int UserId, string FullName, Role Role);
public record UpdateProfileDto([Required, StringLength(100)] string FullName, string? PhoneNumber);
public record ChangePasswordDto([Required] string OldPassword, [Required, MinLength(6)] string NewPassword);
public record UserDto(int Id, string FullName, string Email, string? PhoneNumber, Role Role, bool IsActive);
public record SetRoleDto(Role Role);
public record SetActiveDto(bool IsActive);

// ---- Category ----
public record CategoryDto(int Id, string Name, string? Description, bool IsActive);
public record SaveCategoryDto([Required, StringLength(100)] string Name, string? Description);

// ---- Ticket ----
public record CreateTicketDto([Required, StringLength(200)] string Title, [Required] string Description,
    int CategoryId, TicketPriority Priority = TicketPriority.Medium);
public record UpdateTicketDto([Required, StringLength(200)] string Title, [Required] string Description,
    int CategoryId, TicketPriority Priority = TicketPriority.Medium);
public record AssignDto(int StaffId, DateTime? DueDate);
public record SetPriorityDto(TicketPriority Priority);
public record ChangeStatusDto(TicketStatus Status, string? Note);

public record TicketDto(int Id, string Code, string Title, string Description, string Category,
    TicketPriority Priority, TicketStatus Status, int CreatedById, string CreatedBy,
    int? AssignedToId, string? AssignedTo, DateTime CreatedAt, DateTime? DueDate,
    DateTime? ResolvedAt, DateTime? ClosedAt)
{
    public static readonly Expression<Func<Ticket, TicketDto>> Projection = t => new TicketDto(
        t.Id, t.Code, t.Title, t.Description, t.Category!.Name, t.Priority, t.Status,
        t.CreatedById, t.CreatedBy!.FullName, t.AssignedToId,
        t.AssignedTo != null ? t.AssignedTo.FullName : null,
        t.CreatedAt, t.DueDate, t.ResolvedAt, t.ClosedAt);
}

public record PagedResult<T>(int Page, int PageSize, int Total, List<T> Items);

// ---- Comment / History / Feedback ----
public record CommentRequest([Required, StringLength(2000)] string Content);
public record CommentDto(int Id, int UserId, string UserName, string Content, DateTime CreatedAt);
public record StatusHistoryDto(string ChangedBy, TicketStatus OldStatus, TicketStatus NewStatus, string? Note, DateTime ChangedAt);
public record AssignmentHistoryDto(string AssignedBy, int? OldAssigneeId, int NewAssigneeId, DateTime AssignedAt);
public record FeedbackRequest([Range(1, 5)] int Rating, [StringLength(1000)] string? Comment);
public record FeedbackDto(int Rating, string? Comment, DateTime CreatedAt);
