using System.Globalization;
using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.IpBilling.UI.Components;

public partial class MunicipioList
{
    [Parameter] public Guid? SelectedMunicipioId { get; set; }
    [Parameter] public EventCallback<Municipio> OnMunicipioSelected { get; set; }
    [Parameter] public EventCallback OnCreateRequested { get; set; }

    private List<Municipio> _municipios = [];
    private bool _loading = true;

    protected override Task OnInitializedAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        _loading = true;
        StateHasChanged();
        _municipios = [.. await Repository.GetAllAsync()];
        _loading = false;
        StateHasChanged();
    }

    private Task SelectMunicipio(Municipio municipio) => OnMunicipioSelected.InvokeAsync(municipio);

    /// <summary>The badge text key for a município's Prefeitura grant-sync state (XD01-395); null shows no badge.</summary>
    internal static string? GrantSyncLabelKey(MunicipioGrantSyncState state) => state switch
    {
        MunicipioGrantSyncState.Pending => "admin.ipBilling.municipios.grantSync.pending",
        MunicipioGrantSyncState.Synced => "admin.ipBilling.municipios.grantSync.synced",
        MunicipioGrantSyncState.Failed => "admin.ipBilling.municipios.grantSync.failed",
        _ => null,
    };

    internal static string GrantSyncBadgeStyle(MunicipioGrantSyncState state) => state switch
    {
        MunicipioGrantSyncState.Synced => "background-color:var(--success);color:var(--on-accent)",
        MunicipioGrantSyncState.Failed => "background-color:var(--danger);color:var(--on-accent)",
        _ => "background-color:var(--warning);color:var(--on-accent)",
    };

    private string GrantSyncTitle(Municipio municipio) => municipio switch
    {
        { GrantSyncState: MunicipioGrantSyncState.Synced, GrantSyncedAt: { } syncedAt } =>
            L.GetString("admin.ipBilling.municipios.grantSync.syncedAt", syncedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
        { GrantSyncError: { Length: > 0 } error } => error,
        { GrantSyncState: MunicipioGrantSyncState.Pending } => L["admin.ipBilling.municipios.grantSync.pendingHint"],
        _ => string.Empty,
    };
}
