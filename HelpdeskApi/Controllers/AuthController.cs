namespace HelpdeskApi.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(AppDbContext db, TokenService tokens) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var email = dto.Email.Trim().ToLower();
        if (await db.Users.AnyAsync(u => u.Email == email))
            throw new AppException(409, "Email đã tồn tại");

        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = email,
            PhoneNumber = dto.PhoneNumber,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password) // luôn role User
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Ok(new AuthResponse(tokens.Create(user), user.Id, user.FullName, user.Role));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var email = dto.Email.Trim().ToLower();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            throw new AppException(401, "Sai email hoặc mật khẩu");
        if (!user.IsActive)
            throw new AppException(403, "Tài khoản đã bị khóa");
        return Ok(new AuthResponse(tokens.Create(user), user.Id, user.FullName, user.Role));
    }
}
