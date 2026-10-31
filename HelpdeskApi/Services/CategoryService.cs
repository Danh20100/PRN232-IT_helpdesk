namespace HelpdeskApi.Services;

public class CategoryService(AppRepository repo)
{
    private static CategoryDto ToDto(Category c) => new(c.Id, c.Name, c.Description, c.IsActive);

    public Task<List<CategoryDto>> ListAsync(bool includeInactive)
    {
        var q = repo.Categories.AsQueryable();
        if (!includeInactive) q = q.Where(c => c.IsActive);
        return q.OrderBy(c => c.Id).Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.IsActive)).ToListAsync();
    }

    public async Task<CategoryDto> CreateAsync(SaveCategoryDto dto)
    {
        var name = dto.Name.Trim();
        if (await repo.Categories.AnyAsync(c => c.Name == name)) throw new AppException(409, "Danh mục đã tồn tại");
        var c = new Category { Name = name, Description = dto.Description };
        repo.Add(c);
        await repo.SaveAsync();
        return ToDto(c);
    }

    public async Task<CategoryDto> UpdateAsync(int id, SaveCategoryDto dto)
    {
        var c = await repo.Categories.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Không tìm thấy danh mục");
        var name = dto.Name.Trim();
        if (await repo.Categories.AnyAsync(x => x.Name == name && x.Id != id)) throw new AppException(409, "Tên danh mục đã tồn tại");
        c.Name = name;
        c.Description = dto.Description;
        await repo.SaveAsync();
        return ToDto(c);
    }

    public async Task<CategoryDto> SetActiveAsync(int id, bool active)
    {
        var c = await repo.Categories.FirstOrDefaultAsync(x => x.Id == id) ?? throw new AppException(404, "Không tìm thấy danh mục");
        c.IsActive = active;
        await repo.SaveAsync();
        return ToDto(c);
    }
}
