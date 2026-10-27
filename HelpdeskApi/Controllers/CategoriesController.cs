namespace HelpdeskApi.Controllers;

[ApiController, Route("api/categories"), Authorize]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var q = db.Categories.AsQueryable();
        if (!User.IsInRole("Admin")) q = q.Where(c => c.IsActive); // User/Staff chỉ thấy danh mục đang dùng
        return Ok(await q.OrderBy(c => c.Id).Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.IsActive)).ToListAsync());
    }

    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(SaveCategoryDto dto)
    {
        var name = dto.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.Name == name)) throw new AppException(409, "Danh mục đã tồn tại");
        var c = new Category { Name = name, Description = dto.Description };
        db.Categories.Add(c);
        await db.SaveChangesAsync();
        return Ok(new CategoryDto(c.Id, c.Name, c.Description, c.IsActive));
    }

    [HttpPut("{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, SaveCategoryDto dto)
    {
        var c = await db.Categories.FindAsync(id) ?? throw new AppException(404, "Không tìm thấy danh mục");
        var name = dto.Name.Trim();
        if (await db.Categories.AnyAsync(x => x.Name == name && x.Id != id)) throw new AppException(409, "Tên danh mục đã tồn tại");
        c.Name = name;
        c.Description = dto.Description;
        await db.SaveChangesAsync();
        return Ok(new CategoryDto(c.Id, c.Name, c.Description, c.IsActive));
    }

    // Không xóa vật lý, chỉ vô hiệu hóa / kích hoạt lại
    [HttpPatch("{id:int}/status"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetActive(int id, SetActiveDto dto)
    {
        var c = await db.Categories.FindAsync(id) ?? throw new AppException(404, "Không tìm thấy danh mục");
        c.IsActive = dto.IsActive;
        await db.SaveChangesAsync();
        return Ok(new CategoryDto(c.Id, c.Name, c.Description, c.IsActive));
    }
}
