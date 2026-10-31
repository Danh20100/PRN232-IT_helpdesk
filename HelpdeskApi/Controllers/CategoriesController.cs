namespace HelpdeskApi.Controllers;

[ApiController, Route("api/categories"), Authorize]
public class CategoriesController(CategoryService categories) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await categories.ListAsync(User.IsInRole("Admin")));

    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(SaveCategoryDto dto) => Ok(await categories.CreateAsync(dto));

    [HttpPut("{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, SaveCategoryDto dto) => Ok(await categories.UpdateAsync(id, dto));

    [HttpPatch("{id:int}/status"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetActive(int id, SetActiveDto dto) => Ok(await categories.SetActiveAsync(id, dto.IsActive));
}
