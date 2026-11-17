using GeoAssets.Core.Models;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Shared.Services.SharedUse;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

/// <summary>
/// An order's targets as a paged grid (XD01-312): status and conflict filters, a page-size choice,
/// sortable headers (real buttons, with <c>aria-sort</c>), a conflict badge per row and
/// <see cref="ServiceOrderPager"/>. Every page is one read of
/// <see cref="IServiceOrderTargetReader.GetPageAsync"/> — the store sorts with an id tiebreak, so
/// paging is deterministic — and never more than the chosen page size. A host adds per-row
/// actions through <see cref="RowActions"/>; the occupancy editor's entry point
/// (<see cref="OrderTargetOccupancyPanel"/>) is one.
/// </summary>
public partial class OrderTargetsTable
{
    private readonly CancellationTokenSource _lifetime = new();

    private OrderTargetsGridState _state = OrderTargetsGridState.Default;
    private string? _loadedOrderId;
    private PagedResult<ServiceOrderTarget>? _page;
    private bool _loadFailed;
    private bool _loading;
    private int _loadSequence;

    /// <summary>The sortable columns, in display order.</summary>
    public static IReadOnlyList<OrderTargetsColumn> Columns { get; } =
    [
        new(OrderTargetSortKeys.Alias, "occupancy.targets.target"),
        new(OrderTargetSortKeys.FeatureId, "orders.targets.feature"),
        new(OrderTargetSortKeys.Status, "occupancy.table.status"),
        new(OrderTargetSortKeys.ConflictLevel, "orders.targets.conflict"),
        new(OrderTargetSortKeys.AddedAt, "orders.targets.addedAt"),
    ];

    /// <summary>The order whose targets are listed.</summary>
    [Parameter, EditorRequired] public string OrderId { get; set; } = null!;

    /// <summary>Localization key of the table's caption.</summary>
    [Parameter] public string CaptionKey { get; set; } = "orders.targets.caption";

    /// <summary>Per-row actions; no actions column when null.</summary>
    [Parameter] public RenderFragment<ServiceOrderTarget>? RowActions { get; set; }

    /// <summary>
    /// The order type's activity catalog, which names the activities in the Activities column
    /// (XD01-316); an id it does not know is shown as it is.
    /// </summary>
    [Parameter] public IReadOnlyList<OrderTargetActivity>? ActivityCatalog { get; set; }

    /// <summary>The target whose row is highlighted, if any.</summary>
    [Parameter] public string? SelectedTargetId { get; set; }

    private bool IsFiltered => _state.Status is not null || _state.Conflict is not null;

    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(OrderId, _loadedOrderId, StringComparison.Ordinal)) return;

        _loadedOrderId = OrderId;
        _state = OrderTargetsGridState.Default;
        await LoadAsync();
    }

    /// <summary>Reads the current page. A read that a newer one overtook is dropped.</summary>
    private async Task LoadAsync()
    {
        var sequence = ++_loadSequence;
        (_loading, _loadFailed) = (true, false);
        try
        {
            var page = await Targets.GetPageAsync(_state.ToQuery(OrderId), _lifetime.Token);
            if (sequence != _loadSequence) return;

            _page = page;
        }
        catch (OperationCanceledException)
        {
            // The component went away while loading.
        }
        catch (Exception)
        {
            if (sequence == _loadSequence) (_page, _loadFailed) = (null, true);
        }
        finally
        {
            if (sequence == _loadSequence) _loading = false;
        }
    }

    /// <summary>Reads the current page again, keeping the filters, sort and page (XD01-313: after a target's work was saved).</summary>
    public async Task ReloadAsync()
    {
        await LoadAsync();
        StateHasChanged();
    }

    private Task ApplyAsync(OrderTargetsGridState next)
    {
        if (next == _state) return Task.CompletedTask;

        _state = next;
        return LoadAsync();
    }

    private Task SortAsync(string sortKey) => ApplyAsync(_state.WithSort(sortKey));

    private Task OnStatusChangedAsync(ChangeEventArgs e) => ApplyAsync(_state.WithStatus(e.Value?.ToString()));

    private Task OnConflictChangedAsync(ChangeEventArgs e) =>
        ApplyAsync(_state.WithConflict(ParseConflict(e.Value?.ToString())));

    private Task OnPageSizeChangedAsync(ChangeEventArgs e) =>
        ParsePageSize(e.Value?.ToString()) is { } size ? ApplyAsync(_state.WithPageSize(size)) : Task.CompletedTask;

    /// <summary>A page-size option value back to the size; anything that is not a whole number is ignored.</summary>
    public static int? ParsePageSize(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size) ? size : null;

    private Task GoToPageAsync(int page) => ApplyAsync(_state.WithPage(page, _page?.TotalCount ?? 0));

    private string SortIndicator(string sortKey) => _state.AriaSort(sortKey) switch
    {
        "ascending" => " ▲",
        "descending" => " ▼",
        _ => string.Empty,
    };

    /// <summary>The work asked on a target, by the names <paramref name="catalog"/> gives its activities.</summary>
    public static string ActivitiesOf(ServiceOrderTarget target, IReadOnlyList<OrderTargetActivity>? catalog)
    {
        ArgumentNullException.ThrowIfNull(target);
        return TargetActivityLabels.Describe(target.ActivityIds, catalog);
    }

    /// <summary>A conflict-level option value back to the level; blank or anything else means no filter.</summary>
    public static OrderTargetConflictLevel? ParseConflict(string? value) =>
        value is not null && Enum.GetNames<OrderTargetConflictLevel>().Contains(value, StringComparer.Ordinal)
            ? Enum.Parse<OrderTargetConflictLevel>(value)
            : null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        base.Dispose(disposing);
    }
}
