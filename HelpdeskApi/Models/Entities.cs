using System.ComponentModel.DataAnnotations;

namespace HelpdeskApi.Models;

public enum Role { User, Staff, Admin }
public enum TicketStatus { Open, InProgress, Pending, Resolved, Closed, Cancelled }
public enum TicketPriority { Low, Medium, High, Critical }

public class User
{
    public int Id { get; set; }
    [MaxLength(100)] public string FullName { get; set; } = "";
    [MaxLength(255)] public string Email { get; set; } = "";
    [MaxLength(255)] public string PasswordHash { get; set; } = "";
    [MaxLength(20)] public string? PhoneNumber { get; set; }
    public Role Role { get; set; } = Role.User;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Category
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Ticket
{
    public int Id { get; set; }
    [MaxLength(20)] public string Code { get; set; } = "";
    [MaxLength(200)] public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public int CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public int? AssignedToId { get; set; }
    public User? AssignedTo { get; set; }
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class TicketComment
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    [MaxLength(2000)] public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TicketStatusHistory
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public int ChangedById { get; set; }
    public User? ChangedBy { get; set; }
    public TicketStatus OldStatus { get; set; }
    public TicketStatus NewStatus { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

public class TicketAssignmentHistory
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public int AssignedById { get; set; }
    public User? AssignedBy { get; set; }
    public int? OldAssigneeId { get; set; }
    public int NewAssigneeId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public class TicketFeedback
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int Rating { get; set; }
    [MaxLength(1000)] public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
