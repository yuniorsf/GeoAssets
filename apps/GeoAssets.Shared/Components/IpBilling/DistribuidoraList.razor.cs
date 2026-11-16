using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace GeoAssets.Shared.Components.IpBilling;

/// <summary>Paged sidebar list of Distribuidoras (XD01-301), sorted by the server (name, then id).</summary>
public partial class DistribuidoraList
{
    public const int PageSize = 25;

    [Parameter] public Guid? SelectedDistribuidoraId { get; set; }
    [Parameter] public EventCallback<Distribuidora> OnDistribuidoraSelected { get; set; }
    [Parameter] public EventCallback OnCreateRequested { get; set; }

    private IReadOnlyList<Distribuidora> _distribuidoras = [];
    private int _totalCount;
    private int _page = 1;
    private bool _loading = true;
    private string? _error;

    protected override Task OnInitializedAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            var result = await Repository.GetPageAsync(_page, PageSize);
            var clamped = IpBillingPager.ClampPage(_page, result.TotalCount, PageSize);
            if (clamped != _page)
            {
                _page = clamped;
                result = await Repository.GetPageAsync(_page, PageSize);
            }

            _distribuidoras = result.Items;
            _totalCount = result.TotalCount;
        }
        catch (Exception ex)
        {
            _distribuidoras = [];
            _totalCount = 0;
            _error = L.GetString("admin.ipBilling.distribuidoras.loadError", ex.Message);
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private Task GoToPageAsync(int page)
    {
        _page = page;
        return RefreshAsync();
    }

    private Task SelectAsync(Distribuidora distribuidora) => OnDistribuidoraSelected.InvokeAsync(distribuidora);

    private Task OnItemKeyDownAsync(KeyboardEventArgs e, Distribuidora distribuidora) =>
        e.Key is "Enter" or " " ? SelectAsync(distribuidora) : Task.CompletedTask;
}
