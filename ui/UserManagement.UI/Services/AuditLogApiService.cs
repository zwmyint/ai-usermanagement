using System.Web;
using UserManagement.UI.Contracts;

namespace UserManagement.UI.Services;

public interface IAuditLogApiService
{
    Task<PagedResult<AuditLogDto>> SearchAsync(int page, int pageSize, Guid? userId, int? action,
        DateTime? from, DateTime? to, string? sortBy, bool sortDescending, CancellationToken ct = default);
}

public class AuditLogApiService : ApiClientBase, IAuditLogApiService
{
    public AuditLogApiService(IHttpClientFactory httpClientFactory) : base(httpClientFactory.CreateClient("Api")) { }

    public Task<PagedResult<AuditLogDto>> SearchAsync(int page, int pageSize, Guid? userId, int? action,
        DateTime? from, DateTime? to, string? sortBy, bool sortDescending, CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["page"] = page.ToString();
        query["pageSize"] = pageSize.ToString();
        if (userId.HasValue) query["userId"] = userId.Value.ToString();
        if (action.HasValue) query["action"] = action.Value.ToString();
        if (from.HasValue) query["from"] = from.Value.ToString("o");
        if (to.HasValue) query["to"] = to.Value.ToString("o");
        if (!string.IsNullOrWhiteSpace(sortBy)) query["sortBy"] = sortBy;
        query["sortDescending"] = sortDescending.ToString();

        return SendAsync<PagedResult<AuditLogDto>>(HttpMethod.Get, $"api/audit-logs?{query}", null, ct);
    }
}
