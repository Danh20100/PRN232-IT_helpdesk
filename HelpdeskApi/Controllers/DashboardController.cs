namespace HelpdeskApi.Controllers;

[ApiController, Route("api/dashboard"), Authorize]
public class DashboardController(TicketService svc) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        => Ok(await svc.DashboardAsync(User.GetId(), User.GetRole(), from, to));

    [HttpGet("sla-overview"), Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> SlaOverview([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        => Ok(await svc.GetSlaOverviewAsync(User.GetId(), User.GetRole(), from, to));

    [HttpGet("sla-tickets"), Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> SlaTickets(
        [FromQuery] string? search,
        [FromQuery] TicketPriority? priority,
        [FromQuery] TicketStatus? status,
        [FromQuery] int? staffId,
        [FromQuery] string? slaStatus,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
        => Ok(await svc.GetSlaTicketsAsync(User.GetId(), User.GetRole(), search, priority, status, staffId, slaStatus, sort, page, pageSize));

    [HttpGet("staff-performance"), Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> StaffPerformance([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        => Ok(await svc.GetStaffPerformanceAsync(User.GetId(), User.GetRole(), from, to));
}
