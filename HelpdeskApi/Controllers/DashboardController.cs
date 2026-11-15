namespace HelpdeskApi.Controllers;

[ApiController, Route("api/dashboard"), Authorize]
public class DashboardController(TicketService svc) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        => Ok(await svc.DashboardAsync(User.GetId(), User.GetRole(), from, to));
}
