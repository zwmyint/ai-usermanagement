using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using UserManagement.UI.Models;
using UserManagement.UI.Services;
using UserManagement.UI.ViewModels.Home;

namespace UserManagement.UI.Controllers;

public class HomeController : Controller
{
    private readonly IDashboardApiService _dashboardApi;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IDashboardApiService dashboardApi, ILogger<HomeController> logger)
    {
        _dashboardApi = dashboardApi;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new HomeIndexViewModel();

        if (User.Identity?.IsAuthenticated == true)
        {
            try
            {
                model.Dashboard = await _dashboardApi.GetSummaryAsync(ct);
            }
            catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
            {
                // The landing page must still render when the API is unreachable.
                _logger.LogWarning(ex, "Unable to load dashboard summary from the API.");
                model.DashboardUnavailable = true;
            }
        }

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
