using UserManagement.UI.Contracts;

namespace UserManagement.UI.Services;

public interface IDashboardApiService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default);
}

public class DashboardApiService : ApiClientBase, IDashboardApiService
{
    public DashboardApiService(IHttpClientFactory httpClientFactory) : base(httpClientFactory.CreateClient("Api")) { }

    public Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default) =>
        SendAsync<DashboardSummaryDto>(HttpMethod.Get, "api/dashboard/summary", null, ct);
}
