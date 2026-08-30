using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.Application.DTOs;
using UserManagement.Application.Interfaces;

namespace UserManagement.API.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = "AuditRead")]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditService _auditService;

    public AuditLogsController(IAuditService auditService) => _auditService = auditService;

    [HttpGet]
    public async Task<ActionResult<Common.ApiResponse<PagedResult<AuditLogDto>>>> Search(
        [FromQuery] AuditLogQueryRequest request, CancellationToken ct)
    {
        var result = await _auditService.SearchAsync(request, ct);
        return Ok(Common.ApiResponse<PagedResult<AuditLogDto>>.Ok(result));
    }
}
