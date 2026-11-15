using HelpdeskApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelpdeskApi.Controllers;

[ApiController, Route("api/notifications"), Authorize]
public class NotificationsController(TicketService svc) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 30) =>
        Ok(await svc.NotificationsAsync(User.GetId(), take));

    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        await svc.MarkNotificationReadAsync(id, User.GetId());
        return NoContent();
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await svc.MarkAllNotificationsReadAsync(User.GetId());
        return NoContent();
    }
}
