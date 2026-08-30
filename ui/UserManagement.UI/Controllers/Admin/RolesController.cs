using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.UI.Services;
using UserManagement.UI.ViewModels.Admin;

namespace UserManagement.UI.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("Admin/Roles")]
public class RolesController : Controller
{
    private readonly IRoleApiService _roleApi;

    public RolesController(IRoleApiService roleApi) => _roleApi = roleApi;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var roles = await _roleApi.GetAllAsync(ct);
        return View(roles);
    }

    [HttpGet("Create")]
    public IActionResult Create() => PartialView("_RoleModal", new RoleEditViewModel());

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoleEditViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var role = await _roleApi.CreateAsync(new Contracts.CreateRoleRequest { Name = model.Name, Description = model.Description }, ct);
            return Json(new { success = true, role });
        }
        catch (ApiException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/Edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var role = await _roleApi.GetByIdAsync(id, ct);
        return PartialView("_RoleModal", new RoleEditViewModel { Id = role.Id, Name = role.Name, Description = role.Description });
    }

    [HttpPost("{id:guid}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, RoleEditViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var role = await _roleApi.UpdateAsync(id, new Contracts.UpdateRoleRequest { Name = model.Name, Description = model.Description }, ct);
            return Json(new { success = true, role });
        }
        catch (ApiException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await _roleApi.DeleteAsync(id, ct);
            return Json(new { success = true });
        }
        catch (ApiException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
