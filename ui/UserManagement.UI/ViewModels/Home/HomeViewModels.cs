using UserManagement.UI.Contracts;

namespace UserManagement.UI.ViewModels.Home;

public class HomeIndexViewModel
{
    public DashboardSummaryDto? Dashboard { get; set; }

    /// <summary>True when the API could not be reached, so the page renders a warning instead of the dashboard.</summary>
    public bool DashboardUnavailable { get; set; }
}
