namespace HelpdeskApi.Controllers;

[ApiController, Route("api/users"), Authorize]
public class UsersController(AppDbContext db) : ControllerBase
{
    private static UserDto ToDto(User u) => new(u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.IsActive);

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var u = await db.Users.FindAsync(User.GetId());
        return u is null ? NotFound() : Ok(ToDto(u));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileDto dto)
    {
        var u = await db.Users.FindAsync(User.GetId()) ?? throw new AppException(404, "Không tìm thấy user");
        u.FullName = dto.FullName.Trim();
        u.PhoneNumber = dto.PhoneNumber;
        await db.SaveChangesAsync();
        return Ok(ToDto(u));
    }

    [HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var u = await db.Users.FindAsync(User.GetId()) ?? throw new AppException(404, "Không tìm thấy user");
        if (!BCrypt.Net.BCrypt.Verify(dto.OldPassword, u.PasswordHash))
            throw new AppException(400, "Mật khẩu cũ không đúng");
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Admin ----
    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<IActionResult> List()
        => Ok(await db.Users.OrderBy(u => u.Id).Select(u => ToDto(u)).ToListAsync());

    [HttpGet("support-staff"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SupportStaff()
        => Ok(await db.Users.Where(u => u.Role == Role.Staff && u.IsActive).Select(u => ToDto(u)).ToListAsync());

    [HttpPatch("{id:int}/role"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetRole(int id, SetRoleDto dto)
    {
        if (id == User.GetId()) throw new AppException(400, "Không thể đổi role của chính mình");
        var u = await db.Users.FindAsync(id) ?? throw new AppException(404, "Không tìm thấy user");
        u.Role = dto.Role;
        await db.SaveChangesAsync();
        return Ok(ToDto(u));
    }

    [HttpPatch("{id:int}/status"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetActive(int id, SetActiveDto dto)
    {
        if (id == User.GetId()) throw new AppException(400, "Không thể khóa chính mình");
        var u = await db.Users.FindAsync(id) ?? throw new AppException(404, "Không tìm thấy user");
        u.IsActive = dto.IsActive;
        await db.SaveChangesAsync();
        return Ok(ToDto(u));
    }
}
