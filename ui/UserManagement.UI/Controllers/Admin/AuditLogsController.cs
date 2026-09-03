using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.UI.Services;

namespace UserManagement.UI.Controllers.Admin;

[Authorize(Policy = "AuditRead")]
[Route("Admin/AuditLogs")]
public class AuditLogsController : Controller
{
    // Must stay index-aligned with the #auditTable column definitions in Views/AuditLogs/Index.cshtml.
    // entityName, ipAddress and message columns are not sortable server-side, so null marks their slots.
    private static readonly string?[] SortColumns = { "timestamp", "action", "userName", null, null, "succeeded", null };

    private readonly IAuditLogApiService _auditApi;

    public AuditLogsController(IAuditLogApiService auditApi) => _auditApi = auditApi;

    [HttpGet("")]
    public IActionResult Index() => View();

    /// <summary>Server-side data source consumed by the DataTables grid on the Audit Logs index page.</summary>
    [HttpGet("Grid")]
    public async Task<IActionResult> Grid(
        [FromQuery] int draw, [FromQuery] int start, [FromQuery(Name = "length")] int pageLength,
        [FromQuery] Guid? userId, [FromQuery] int? action, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery(Name = "order[0][column]")] int? orderColumn,
        [FromQuery(Name = "order[0][dir]")] string? orderDir,
        CancellationToken ct)
    {
        var pageSize = pageLength <= 0 ? 10 : pageLength;
        var page = (start / pageSize) + 1;
        var sortBy = orderColumn.HasValue && orderColumn.Value >= 0 && orderColumn.Value < SortColumns.Length
            ? SortColumns[orderColumn.Value]
            : null;
        var sortDescending = string.Equals(orderDir, "desc", StringComparison.OrdinalIgnoreCase);

        var result = await _auditApi.SearchAsync(page, pageSize, userId, action, from, to, sortBy, sortDescending, ct);

        return Json(new
        {
            draw,
            recordsTotal = result.TotalCount,
            recordsFiltered = result.FilteredCount,
            data = result.Items
        });
    }
}
