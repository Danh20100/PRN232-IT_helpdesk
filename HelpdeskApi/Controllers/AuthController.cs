namespace HelpdeskApi.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(UserService users, TokenService tokens) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var user = await users.RegisterAsync(dto);
        return Ok(new AuthResponse(tokens.Create(user), user.Id, user.FullName, user.Role));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var user = await users.LoginAsync(dto);
        return Ok(new AuthResponse(tokens.Create(user), user.Id, user.FullName, user.Role));
    }
}
