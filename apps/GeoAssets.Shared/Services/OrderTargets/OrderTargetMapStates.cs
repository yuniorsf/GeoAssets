using GeoAssets.Shared.Interfaces;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// Shows the targets of one service order on the current map (XD01-313): each target's feature
/// gets the <see cref="MapFeatureState"/> <see cref="OrderTargetMapState.Of"/> decides, so the
/// work still to do, the work done, conflicts and blocked targets can be told apart at a glance.
///
/// <list type="bullet">
/// <item><description>
/// The targets are read a page of <see cref="PageSize"/> at a time through
/// <see cref="IServiceOrderTargetReader.GetPageAsync"/> and each page is pushed as it arrives, so
/// no single read or map call grows with the order.
/// </description></item>
/// <item><description>
/// One order at a time, owned by whoever showed it: showing another order — from any owner —
/// first takes the previous one off the map, and <see cref="ClearAsync"/> does nothing unless the
/// caller is the current owner, so a view that goes away cannot clear what a newer view shows.
/// </description></item>
/// <item><description>
/// Taking an order off the map touches only the features it styled: a feature that is in the map
/// selection goes back to <see cref="MapFeatureState.Selected"/>, any other to
/// <see cref="MapFeatureState.Default"/>. The selection itself is the
/// <c>MapSelectionCoordinator</c>'s; while an order is shown, selecting or unselecting one of its
/// targets restyles that feature as the selection says.
/// </description></item>
/// <item><description>
/// Best effort: the map is a view of the order, so a failure is logged and never thrown.
/// Cancellation — by the caller, or because another order was shown — stops quietly.
/// </description></item>
/// </list>
/// Scoped; like the selection services it must be used from the renderer's synchronization context.
/// </summary>
public sealed class OrderTargetMapStates(
    IServiceOrderTargetReader targets,
    IMapSelectionInterop map,
    IMapSelectionSet selection,
    ICurrentMapContext mapContext,
    ILogger<OrderTargetMapStates> logger)
{
    /// <summary>How many targets are read, and pushed, per round trip.</summary>
    public const int PageSize = OrderTargetQuery.MaxPageSize;

    private readonly Dictionary<string, MapFeatureState> _shown = new(StringComparer.Ordinal);
    private readonly HashSet<string> _refreshed = new(StringComparer.Ordinal);
    private object? _owner;
    private string? _divId;
    private CancellationTokenSource? _loading;
    private int _showSequence;

    /// <summary>The order whose targets are on the map, or null.</summary>
    public string? OrderId { get; private set; }

    /// <summary>The state each feature of the shown order was given, by feature id.</summary>
    public IReadOnlyDictionary<string, MapFeatureState> Shown => _shown;

    /// <summary>
    /// Takes any order off the map, then puts <paramref name="orderId"/>'s targets on it, page by
    /// page. Returns true when every page was pushed; false when it was cancelled, or failed
    /// (logged) — whatever was pushed by then stays until the order is cleared or replaced.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="orderId"/> is null or blank.</exception>
    public async Task<bool> ShowAsync(object owner, string orderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var sequence = ++_showSequence;
        await ResetAsync();
        if (sequence != _showSequence) return false;   // another order was shown meanwhile

        var divId = mapContext.MapDivId;
        var loading = CancellationTokenSource.CreateLinkedTokenSource(ct);
        (_owner, OrderId, _divId, _loading) = (owner, orderId, divId, loading);
        try
        {
            for (var page = 1; ; page++)
            {
                var query = new OrderTargetQuery(orderId, page, PageSize) { SortBy = OrderTargetSortKeys.FeatureId };
                var result = await targets.GetPageAsync(query, loading.Token);
                await PushAsync(divId, result.Items, loading.Token);

                if (result.Items.Count == 0 || (long)page * PageSize >= result.TotalCount) return true;
            }
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not show the targets of order {OrderId} on the map", orderId);
            return false;
        }
        finally
        {
            if (ReferenceEquals(_loading, loading)) _loading = null;
            loading.Dispose();
        }
    }

    /// <summary>
    /// Restyles one target after its work changed, when it belongs to the shown order (otherwise
    /// nothing happens). A page still being loaded will not overwrite it with what it read before.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    public async Task RefreshAsync(ServiceOrderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_divId is null || !string.Equals(target.OrderId, OrderId, StringComparison.Ordinal)) return;

        var state = OrderTargetMapState.Of(target);
        _shown[target.FeatureId] = state;
        _refreshed.Add(target.FeatureId);
        try
        {
            await map.SetFeatureStatesAsync(_divId, [target.FeatureId], state);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not restyle a target of order {OrderId} on the map", target.OrderId);
        }
    }

    /// <summary>Takes the shown order off the map, when <paramref name="owner"/> is the one that showed it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
    public Task ClearAsync(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return ReferenceEquals(owner, _owner) ? ResetAsync() : Task.CompletedTask;
    }

    /// <summary>Pushes one page, a call per state; ids restyled since the load began keep their newer state.</summary>
    private async Task PushAsync(string divId, IReadOnlyList<ServiceOrderTarget> page, CancellationToken ct)
    {
        foreach (var group in page.Where(t => !_refreshed.Contains(t.FeatureId)).GroupBy(OrderTargetMapState.Of))
        {
            // Checked before recording, with no await in between: a newer order owns _shown once this is cancelled.
            ct.ThrowIfCancellationRequested();

            string[] ids = [.. group.Select(t => t.FeatureId)];
            foreach (var id in ids) _shown[id] = group.Key;
            await map.SetFeatureStatesAsync(divId, ids, group.Key);
        }
    }

    /// <summary>Stops a load in progress and gives every feature the shown order styled its selection state back.</summary>
    private async Task ResetAsync()
    {
        if (_loading is { } running)
        {
            _loading = null;
            await running.CancelAsync();
        }

        var divId = _divId;
        string[] shown = [.. _shown.Keys];
        (_owner, OrderId, _divId) = (null, null, null);
        _shown.Clear();
        _refreshed.Clear();
        if (divId is null || shown.Length == 0) return;

        var selectedIds = new HashSet<string>(selection.Ids, StringComparer.Ordinal);
        string[] selected = [.. shown.Where(selectedIds.Contains)];
        string[] unselected = [.. shown.Where(id => !selectedIds.Contains(id))];
        try
        {
            if (unselected.Length > 0) await map.SetFeatureStatesAsync(divId, unselected, MapFeatureState.Default);
            if (selected.Length > 0) await map.SetFeatureStatesAsync(divId, selected, MapFeatureState.Selected);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not take the targets of an order off the map");
        }
    }
}
