namespace HelpdeskApi.Services;

public class UserService(AppRepository repo)
{
    private static UserDto ToDto(User u) => new(u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.IsActive);

    public async Task<User> RegisterAsync(RegisterDto dto)
    {
        var email = dto.Email.Trim().ToLower();
        if (await repo.Users.AnyAsync(u => u.Email == email))
            throw new AppException(409, "Email đã tồn tại");
        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = email,
            PhoneNumber = dto.PhoneNumber,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
        };
        repo.Add(user);
        await repo.SaveAsync();
        return user;
    }

    public async Task<User> LoginAsync(LoginDto dto)
    {
        var email = dto.Email.Trim().ToLower();
        var user = await repo.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            throw new AppException(401, "Sai email hoặc mật khẩu");
        if (!user.IsActive) throw new AppException(403, "Tài khoản đã bị khóa");
        return user;
    }

    public async Task<UserDto?> MeAsync(int id)
    {
        var u = await repo.Users.FirstOrDefaultAsync(x => x.Id == id);
        return u is null ? null : ToDto(u);
    }

    public async Task<UserDto> UpdateMeAsync(int id, UpdateProfileDto dto)
    {
        var u = await repo.Users.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Không tìm thấy user");
        u.FullName = dto.FullName.Trim();
        u.PhoneNumber = dto.PhoneNumber;
        await repo.SaveAsync();
        return ToDto(u);
    }

    public async Task ChangePasswordAsync(int id, ChangePasswordDto dto)
    {
        var u = await repo.Users.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Không tìm thấy user");
        if (!BCrypt.Net.BCrypt.Verify(dto.OldPassword, u.PasswordHash))
            throw new AppException(400, "Mật khẩu cũ không đúng");
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await repo.SaveAsync();
    }

    public Task<List<UserDto>> ListAsync()
        => repo.Users.OrderBy(u => u.Id).Select(u => new UserDto(u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.IsActive)).ToListAsync();

    public Task<List<UserDto>> StaffAsync()
        => repo.Users.Where(u => u.Role == Role.Staff && u.IsActive)
            .Select(u => new UserDto(u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.IsActive)).ToListAsync();

    public async Task<UserDto> SetRoleAsync(int id, int actorId, Role role)
    {
        if (id == actorId) throw new AppException(400, "Không thể đổi role của chính mình");
        var u = await repo.Users.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Không tìm thấy user");
        u.Role = role;
        await repo.SaveAsync();
        return ToDto(u);
    }

    public async Task<UserDto> SetActiveAsync(int id, int actorId, bool active)
    {
        if (id == actorId) throw new AppException(400, "Không thể khóa chính mình");
        var u = await repo.Users.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Không tìm thấy user");
        u.IsActive = active;
        await repo.SaveAsync();
        return ToDto(u);
    }
}
