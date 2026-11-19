using System.Globalization;
using GeoAssets.IpBilling.Models;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.IpBilling.UI.Components;

/// <summary>
/// Paged editor for one Distribuidora's lamp wattage chart (XD01-301) — the chart
/// Fiscalização/Ofício validation converts inspected points to kWh with. Saving a
/// <c>(lamp type, wattage)</c> that already exists updates that row's ballast loss in place
/// (the repository's upsert), so the same small form both adds and edits. Rows arrive sorted
/// by the server: lamp type, wattage, then id.
/// </summary>
public partial class DistribuidoraLampWattageEditor
{
    public const int DefaultPageSize = 25;
    private static readonly int[] PageSizeOptions = [10, 25, 50, 100];

    [Parameter, EditorRequired] public Guid DistribuidoraId { get; set; }

    private IReadOnlyList<DistribuidoraLampWattage> _rows = [];
    private int _totalCount;
    private int _page = 1;
    private int _pageSize = DefaultPageSize;
    private bool _loading = true;
    private bool _busy;
    private string? _error;
    private Guid? _loadedDistribuidoraId;

    private LampTypeCode _code = LampTypeCode.VS;
    private int _wattageW;
    private decimal _ballastLossW;

    /// <summary>
    /// The i18n key of the validation problem with a candidate chart row, or <c>null</c> when
    /// it can be saved. Pure, so the form's rules are unit-testable without rendering.
    /// </summary>
    public static string? ValidateRow(int wattageW, decimal ballastLossW) =>
        wattageW <= 0 ? "admin.ipBilling.distribuidoras.wattageInvalid"
        : ballastLossW < 0 ? "admin.ipBilling.distribuidoras.ballastLossInvalid"
        : null;

    protected override Task OnParametersSetAsync()
    {
        if (DistribuidoraId == _loadedDistribuidoraId) return Task.CompletedTask;

        _loadedDistribuidoraId = DistribuidoraId;
        _page = 1;
        _rows = [];
        _totalCount = 0;
        _error = null;
        return LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        _loading = true;
        try
        {
            var result = await Repository.GetPageAsync(DistribuidoraId, _page, _pageSize);
            var clamped = IpBillingPager.ClampPage(_page, result.TotalCount, _pageSize);
            if (clamped != _page)
            {
                _page = clamped;
                result = await Repository.GetPageAsync(DistribuidoraId, _page, _pageSize);
            }

            _rows = result.Items;
            _totalCount = result.TotalCount;
        }
        catch (Exception ex)
        {
            _error = L.GetString("admin.ipBilling.distribuidoras.loadError", ex.Message);
        }
        finally
        {
            _loading = false;
        }
    }

    private Task GoToPageAsync(int page)
    {
        _page = page;
        return LoadPageAsync();
    }

    private Task OnPageSizeChangedAsync(ChangeEventArgs e)
    {
        _pageSize = int.TryParse(e.Value?.ToString(), CultureInfo.InvariantCulture, out var size) ? size : DefaultPageSize;
        _page = 1;
        return LoadPageAsync();
    }

    private void EditRow(DistribuidoraLampWattage row)
    {
        _code = row.Code;
        _wattageW = row.WattageW;
        _ballastLossW = row.BallastLossW;
    }

    private async Task HandleUpsertAsync()
    {
        _error = null;
        if (ValidateRow(_wattageW, _ballastLossW) is { } problemKey)
        {
            _error = L[problemKey];
            return;
        }

        _busy = true;
        try
        {
            await Repository.UpsertAsync(new DistribuidoraLampWattage
            {
                DistribuidoraId = DistribuidoraId,
                Code = _code,
                WattageW = _wattageW,
                BallastLossW = _ballastLossW,
            });
            await Repository.SaveChangesAsync();
            await LoadPageAsync();
        }
        catch (Exception ex)
        {
            _error = L.GetString("admin.ipBilling.distribuidoras.saveError", ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task HandleDeleteAsync(DistribuidoraLampWattage row)
    {
        _error = null;
        _busy = true;
        try
        {
            await Repository.DeleteAsync(row.Id);
            await Repository.SaveChangesAsync();
            await LoadPageAsync();
        }
        catch (Exception ex)
        {
            _error = L.GetString("admin.ipBilling.distribuidoras.saveError", ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }
}
