using GeoAssets.Shared.Services.ReferenceLayers;

namespace GeoAssets.Shared.Components.Layers;

/// <summary>
/// One switch per organization whose assets the caller may show as a reference layer (XD01-331) — "Reference: COELBA" —
/// off by default, the state kept by <see cref="ReferenceLayerService"/> for the session, so closing and reopening the
/// layers panel keeps it. Renders nothing for a caller with no such organization. An organization the server stops
/// opening mid-session disappears from the list without an error (<see cref="ReferenceLayerService.Changed"/>).
/// </summary>
public partial class ReferenceLayerToggles
{
    private readonly CancellationTokenSource _disposed = new();
    private IReadOnlyList<ReferenceOrganization> _organizations = [];

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ReferenceLayers.Changed += OnReferenceLayersChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _organizations = await ReferenceLayers.LoadOrganizationsAsync(_disposed.Token);
        }
        catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
        {
            // The panel closed before the entitlements arrived.
        }
    }

    private Task ToggleAsync(ReferenceOrganization organization, object? value) =>
        ReferenceLayers.SetEnabledAsync(organization.Id, value is true);

    private void OnReferenceLayersChanged(object? sender, EventArgs e) =>
        _ = InvokeAsync(() =>
        {
            _organizations = ReferenceLayers.Organizations;
            StateHasChanged();
        });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReferenceLayers.Changed -= OnReferenceLayersChanged;
            _disposed.Cancel();
            _disposed.Dispose();
        }

        base.Dispose(disposing);
    }
}
