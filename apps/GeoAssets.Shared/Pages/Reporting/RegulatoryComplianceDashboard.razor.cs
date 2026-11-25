using GeoAssets.Workflow.SlaGuard;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Pages.Reporting;

public partial class RegulatoryComplianceDashboard
{
    [Inject] private ILogger<RegulatoryComplianceDashboard> Logger { get; set; } = null!;
    [Inject] private ISlaBreachQueryService BreachQuery { get; set; } = null!;

    private bool? _authorized;
    private SlaBreachSummary? _summary;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _authorized = await AuthService.HasPermissionAsync("serviceorders:read");
        }
        catch (Exception ex)
        {
            // Fail closed: if the authorization check itself can't be answered (e.g. the
            // identity backend is unreachable), don't show reporting data.
            Logger.LogWarning(ex, "Failed to resolve authorization");
            _authorized = false;
            return;
        }

        if (_authorized == true)
        {
            _summary = await BreachQuery.GetLiveSummaryAsync();
        }
    }
}
