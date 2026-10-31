namespace HelpdeskApi.Data;

public static class DbSeeder
{
    /// <summary>Categories đã có trong db/ITHelpdesk_DatabaseFirst.sql. Ở đây chỉ tạo tài khoản mẫu với hash BCrypt thật.</summary>
    public static void Seed(AppDbContext db)
    {
        if (db.Users.Any()) return;

        User Make(string name, string email, string pass, Role role) => new()
        {
            FullName = name, Email = email, Role = role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(pass)
        };
        db.Users.AddRange(
            Make("Admin", "admin@helpdesk.local", "Admin@123", Role.Admin),
            Make("Staff A", "staff1@helpdesk.local", "Staff@123", Role.Staff),
            Make("Staff B", "staff2@helpdesk.local", "Staff@123", Role.Staff),
            Make("Nguyen Van A", "user1@helpdesk.local", "User@123", Role.User),
            Make("Tran Thi B", "user2@helpdesk.local", "User@123", Role.User));
        db.SaveChanges();
    }
}
