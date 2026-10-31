namespace HelpdeskApi.Controllers;

[ApiController, Route("api/users"), Authorize]
public class UsersController(UserService users) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var u = await users.MeAsync(User.GetId());
        return u is null ? NotFound() : Ok(u);
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileDto dto) => Ok(await users.UpdateMeAsync(User.GetId(), dto));

    [HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        await users.ChangePasswordAsync(User.GetId(), dto);
        return NoContent();
    }

    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<IActionResult> List() => Ok(await users.ListAsync());

    [HttpGet("support-staff"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SupportStaff() => Ok(await users.StaffAsync());

    [HttpPatch("{id:int}/role"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetRole(int id, SetRoleDto dto) => Ok(await users.SetRoleAsync(id, User.GetId(), dto.Role));

    [HttpPatch("{id:int}/status"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetActive(int id, SetActiveDto dto) => Ok(await users.SetActiveAsync(id, User.GetId(), dto.IsActive));
}
