using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Pages.Isp;

public partial class TenantPortal
{
    [Inject] private ITenantPortalRestClient Client { get; set; } = null!;

    private IReadOnlyList<ServiceOrder> _attachmentRequests = [];
    private IReadOnlyList<ServiceOrder> _flaggedNotices = [];
    private string _newRequestPoleFeatureId = string.Empty;
    private string _newRequestDescription = string.Empty;
    private bool _submittingRequest;
    private string? _disputingOrderId;
    private string _disputeEvidenceReference = string.Empty;
    private string? _loadError;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _attachmentRequests = await Client.GetMyAttachmentRequestsAsync();
            _flaggedNotices = await Client.GetFlaggedNoticesAsync();
            _loadError = null;
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
        }
    }

    private async Task SubmitAttachmentRequestAsync()
    {
        if (string.IsNullOrWhiteSpace(_newRequestPoleFeatureId)) return;

        _submittingRequest = true;
        try
        {
            await Client.SubmitAttachmentRequestAsync(_newRequestPoleFeatureId, _newRequestDescription);
            _newRequestPoleFeatureId = string.Empty;
            _newRequestDescription = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
        }
        finally
        {
            _submittingRequest = false;
        }
    }

    private async Task SubmitDisputeAsync()
    {
        if (_disputingOrderId is null || string.IsNullOrWhiteSpace(_disputeEvidenceReference)) return;

        try
        {
            await Client.SubmitDisputeAsync(_disputingOrderId, _disputeEvidenceReference);
            _disputingOrderId = null;
            _disputeEvidenceReference = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
        }
    }
}
