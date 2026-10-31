namespace HelpdeskApi.Controllers;

[ApiController, Route("api/tickets"), Authorize]
public class TicketsController(TicketService svc) : ControllerBase
{
    [HttpPost, Authorize(Roles = "User")]
    public async Task<IActionResult> Create(CreateTicketDto dto)
    {
        var t = await svc.CreateAsync(User.GetId(), dto);
        return CreatedAtAction(nameof(Get), new { id = t.Id }, t);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] TicketStatus? status,
        [FromQuery] int? categoryId, [FromQuery] TicketPriority? priority, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(await svc.ListAsync(User.GetId(), User.GetRole(), search, status, categoryId, priority, sort, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await svc.GetDtoAsync(id, User.GetId(), User.GetRole()));

    [HttpPut("{id:int}"), Authorize(Roles = "User")]
    public async Task<IActionResult> Update(int id, UpdateTicketDto dto)
        => Ok(await svc.UpdateAsync(id, User.GetId(), dto));

    [HttpDelete("{id:int}"), Authorize(Roles = "User")]
    public async Task<IActionResult> Cancel(int id)
    {
        await svc.ChangeStatusAsync(id, User.GetId(), User.GetRole(), TicketStatus.Cancelled, null);
        return NoContent();
    }

    [HttpPatch("{id:int}/assign"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Assign(int id, AssignDto dto)
        => Ok(await svc.AssignAsync(id, User.GetId(), dto.StaffId, dto.DueDate));

    [HttpPatch("{id:int}/priority"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetPriority(int id, SetPriorityDto dto)
        => Ok(await svc.SetPriorityAsync(id, dto.Priority));

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(int id, ChangeStatusDto dto)
        => Ok(await svc.ChangeStatusAsync(id, User.GetId(), User.GetRole(), dto.Status, dto.Note));

    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> History(int id) => Ok(await svc.HistoryAsync(id, User.GetId(), User.GetRole()));

    [HttpGet("{id:int}/assignments")]
    public async Task<IActionResult> Assignments(int id) => Ok(await svc.AssignmentsAsync(id, User.GetId(), User.GetRole()));

    [HttpGet("{id:int}/comments")]
    public async Task<IActionResult> Comments(int id) => Ok(await svc.CommentsAsync(id, User.GetId(), User.GetRole()));

    [HttpPost("{id:int}/comments")]
    public async Task<IActionResult> AddComment(int id, CommentRequest dto)
        => Ok(await svc.AddCommentAsync(id, User.GetId(), User.GetRole(), dto.Content));

    [HttpPost("{id:int}/feedback"), Authorize(Roles = "User")]
    public async Task<IActionResult> AddFeedback(int id, FeedbackRequest dto)
        => Ok(await svc.AddFeedbackAsync(id, User.GetId(), dto));

    [HttpGet("{id:int}/feedback")]
    public async Task<IActionResult> GetFeedback(int id)
    {
        var f = await svc.FeedbackAsync(id, User.GetId(), User.GetRole());
        return f is null ? NotFound() : Ok(f);
    }
}
