using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.UI.Contracts;
using UserManagement.UI.Services;
using UserManagement.UI.ViewModels.Admin;

namespace UserManagement.UI.Controllers.Admin;

[Authorize(Policy = "UsersRead")]
[Route("Admin/Users")]
public class UsersController : Controller
{
    // Must stay index-aligned with the #usersTable column definitions in Views/Users/Index.cshtml.
    // The "roles" (3) and "actions" (6) columns are not orderable, so null marks their slots.
    private static readonly string?[] SortColumns = { "userName", "email", "fullName", null, "isActive", "createdAt", null };

    private readonly IUserApiService _userApi;
    private readonly IRoleApiService _roleApi;

    public UsersController(IUserApiService userApi, IRoleApiService roleApi)
    {
        _userApi = userApi;
        _roleApi = roleApi;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Roles = await _roleApi.GetAllAsync(ct);
        return View();
    }

    /// <summary>Server-side data source consumed by the DataTables grid on the Users index page.</summary>
    [HttpGet("Grid")]
    public async Task<IActionResult> Grid(
        [FromQuery] int draw, [FromQuery] int start, [FromQuery(Name = "length")] int pageLength,
        [FromQuery(Name = "search[value]")] string? searchValue,
        [FromQuery(Name = "order[0][column]")] int? orderColumn,
        [FromQuery(Name = "order[0][dir]")] string? orderDir,
        [FromQuery] string? role, [FromQuery] bool? isActive,
        CancellationToken ct)
    {
        var pageSize = pageLength <= 0 ? 10 : pageLength;
        var page = (start / pageSize) + 1;
        var sortBy = orderColumn.HasValue && orderColumn.Value >= 0 && orderColumn.Value < SortColumns.Length
            ? SortColumns[orderColumn.Value] ?? "createdAt"
            : "createdAt";
        var sortDescending = string.Equals(orderDir, "desc", StringComparison.OrdinalIgnoreCase);

        var result = await _userApi.GetUsersAsync(page, pageSize, searchValue, role, isActive, sortBy, sortDescending, ct);

        return Json(new
        {
            draw,
            recordsTotal = result.TotalCount,
            recordsFiltered = result.FilteredCount,
            data = result.Items
        });
    }

    [HttpGet("Create")]
    [Authorize(Policy = "UsersWrite")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewBag.Roles = await _roleApi.GetAllAsync(ct);
        return PartialView("_CreateModal", new UserCreateViewModel());
    }

    [HttpPost("Create")]
    [Authorize(Policy = "UsersWrite")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var created = await _userApi.CreateAsync(new CreateUserRequest
            {
                UserName = model.UserName,
                Email = model.Email,
                Password = model.Password,
                FirstName = model.FirstName,
                LastName = model.LastName,
                PhoneNumber = model.PhoneNumber,
                IsActive = model.IsActive,
                Roles = model.Roles
            }, ct);
            if (model.ProfilePicture is { Length: > 0 })
                created = await _userApi.UploadProfilePictureAsync(created.Id, model.ProfilePicture, ct);

            return Json(new { success = true, user = created });
        }
        catch (ApiException ex)
        {
            return BadRequest(new { success = false, message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpGet("{id:guid}/Edit")]
    [Authorize(Policy = "UsersWrite")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var user = await _userApi.GetByIdAsync(id, ct);
        ViewBag.Roles = await _roleApi.GetAllAsync(ct);
        ViewBag.UserRoles = user.Roles;
        ViewBag.UserId = user.Id;

        var model = new UserEditViewModel
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive
        };

        return PartialView("_EditModal", model);
    }

    [HttpPost("{id:guid}/Edit")]
    [Authorize(Policy = "UsersWrite")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, UserEditViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var updated = await _userApi.UpdateAsync(id, new UpdateUserRequest
            {
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                PhoneNumber = model.PhoneNumber,
                IsActive = model.IsActive
            }, ct);
            if (model.ProfilePicture is { Length: > 0 })
                updated = await _userApi.UploadProfilePictureAsync(id, model.ProfilePicture, ct);

            return Json(new { success = true, user = updated });
        }
        catch (ApiException ex)
        {
            return BadRequest(new { success = false, message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("{id:guid}/Delete")]
    [Authorize(Policy = "UsersDelete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await _userApi.DeleteAsync(id, ct);
            return Json(new { success = true });
        }
        catch (ApiException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/ToggleActive")]
    [Authorize(Policy = "UsersWrite")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(Guid id, [FromForm] bool isActive, CancellationToken ct)
    {
        try
        {
            var user = await _userApi.SetActiveAsync(id, isActive, ct);
            return Json(new { success = true, user });
        }
        catch (ApiException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/Roles")]
    [Authorize(Policy = "UsersWrite")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignRoles(Guid id, [FromForm] List<string> roles, CancellationToken ct)
    {
        try
        {
            var user = await _userApi.AssignRolesAsync(id, roles ?? new List<string>(), ct);
            return Json(new { success = true, user });
        }
        catch (ApiException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
