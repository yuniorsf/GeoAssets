using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Drives the map's selection tools for one map (XD01-310): routes clicks and area gestures into
/// the <see cref="IMapSelectionSet"/>, and mirrors every change of that set onto the map as
/// <see cref="MapFeatureState.Selected"/> / <see cref="MapFeatureState.Default"/> feature states.
/// Owned by the component that owns the map (<c>MapWorkspace</c>), not by DI — it is bound to one
/// map div and is disposed with it. Like the set, it must be used from the renderer's
/// synchronization context.
/// </summary>
public sealed class MapSelectionCoordinator : IDisposable
{
    private readonly IMapSelectionSet _selection;
    private readonly IMapSelectionResolver _resolver;
    private readonly IMapSelectionInterop _interop;
    private readonly ILogger<MapSelectionCoordinator> _logger;
    private readonly string _divId;
    private readonly MapSelectionFenceMemory? _fenceMemory;

    private CancellationTokenSource? _resolveCts;
    private Task _statePushes = Task.CompletedTask;
    private bool _disposed;

    public MapSelectionCoordinator(
        IMapSelectionSet selection,
        IMapSelectionResolver resolver,
        IMapSelectionInterop interop,
        ILogger<MapSelectionCoordinator> logger,
        string divId,
        MapSelectionFenceMemory? fenceMemory = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(divId);

        _selection = selection;
        _resolver = resolver;
        _interop = interop;
        _logger = logger;
        _divId = divId;
        _fenceMemory = fenceMemory;

        _selection.Changed += OnSelectionChanged;
    }

    /// <summary>The armed tool.</summary>
    public MapSelectionTool Tool { get; private set; }

    /// <summary>Whether an area gesture is being resolved.</summary>
    public bool IsResolving { get; private set; }

    /// <summary>The message to show for the last click or gesture, if any. A new gesture, or
    /// <see cref="DismissNotice"/>, clears it.</summary>
    public MapSelectionNotice? Notice { get; private set; }

    /// <summary>Raised when <see cref="Tool"/>, <see cref="IsResolving"/>, <see cref="Notice"/> or
    /// the selection itself changed — everything a toolbar renders.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Completes when every feature-state push queued so far has reached the map. Pushes run one
    /// after another in the order the selection changed, so a batched push can never interleave
    /// with the next change's.
    /// </summary>
    public Task PendingStatePushes => _statePushes;

    /// <summary>Arms <paramref name="tool"/> on the map (or disarms with <see cref="MapSelectionTool.None"/>).</summary>
    public async Task SetToolAsync(MapSelectionTool tool)
    {
        if (_disposed) return;

        await _interop.SetSelectionToolAsync(_divId, tool);
        Tool = tool;
        RaiseStateChanged();
    }

    /// <summary>
    /// A feature was clicked. With <see cref="MapSelectionTool.ClickToggle"/> armed the feature is
    /// toggled in the selection and <c>true</c> is returned; with any other tool nothing happens
    /// and <c>false</c> tells the caller to run its normal feature-click behaviour.
    /// </summary>
    public bool HandleClick(string featureId)
    {
        ArgumentNullException.ThrowIfNull(featureId);
        if (_disposed || Tool != MapSelectionTool.ClickToggle) return false;

        Notice = null;
        Report(_selection.Toggle(featureId));
        return true;
    }

    /// <summary>
    /// A box or fence was drawn: resolves it to feature ids and applies them — added to the
    /// selection when the gesture is additive, replacing it otherwise. A newer gesture cancels this
    /// one, and a result that arrives after that is discarded. On failure the selection is left
    /// exactly as it was and <see cref="Notice"/> says why.
    /// </summary>
    public async Task HandleGestureAsync(MapSelectionGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (_disposed) return;

        var cts = new CancellationTokenSource();
        if (Interlocked.Exchange(ref _resolveCts, cts) is { } superseded)
            await superseded.CancelAsync();

        Notice = null;
        IsResolving = true;
        RaiseStateChanged();

        try
        {
            var ids = await _resolver.ResolveAsync(gesture, cts.Token);
            if (cts.IsCancellationRequested) return;

            if (gesture.Additive)
            {
                Report(_selection.Add(ids));
                return;
            }

            var change = _selection.Replace(ids);
            if (change.Overflow == 0) RememberFence(gesture);
            Report(change);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogDebug("Map selection gesture superseded or cancelled before it resolved");
        }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested) return;

            Notice = Describe(ex);
            _logger.LogWarning(ex, "Map selection gesture failed — {Kind}", Notice.Kind);
        }
        finally
        {
            // Only the newest gesture owns the busy flag; a superseded one must not clear it.
            if (Interlocked.CompareExchange(ref _resolveCts, null, cts) == cts)
            {
                IsResolving = false;
                RaiseStateChanged();
            }

            cts.Dispose();
        }
    }

    /// <summary>Empties the selection.</summary>
    public void Clear()
    {
        if (_disposed) return;

        Notice = null;
        if (_selection.Clear().IsEmpty) RaiseStateChanged();
    }

    /// <summary>Hides the current <see cref="Notice"/>.</summary>
    public void DismissNotice()
    {
        if (Notice is null) return;

        Notice = null;
        RaiseStateChanged();
    }

    /// <summary>Maps a resolver failure to the message the user sees.</summary>
    public static MapSelectionNotice Describe(Exception exception) => exception switch
    {
        SelectionTooLargeException tooLarge => new MapSelectionNotice(MapSelectionNoticeKind.TooLarge, tooLarge.MatchedAtLeast, tooLarge.Ceiling),
        InvalidSelectionFenceException => new MapSelectionNotice(MapSelectionNoticeKind.InvalidArea),
        NotSupportedException => new MapSelectionNotice(MapSelectionNoticeKind.Unsupported),
        _ => new MapSelectionNotice(MapSelectionNoticeKind.Failed)
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _selection.Changed -= OnSelectionChanged;

        // The gesture that owns the source disposes it when its resolve unwinds.
        Interlocked.Exchange(ref _resolveCts, null)?.Cancel();
    }

    /// <summary>
    /// Records the gesture's fence as the origin of the selection it just replaced (XD01-312), in
    /// the normalised form the resolver sent. A gesture the normaliser rejects — only possible with
    /// a resolver that does not normalise itself — is simply not remembered.
    /// </summary>
    private void RememberFence(MapSelectionGesture gesture)
    {
        if (_fenceMemory is null) return;

        try
        {
            _fenceMemory.Remember(MapSelectionRequest.FromGesture(gesture).Rings);
        }
        catch (Exception ex) when (ex is InvalidSelectionFenceException or ArgumentException)
        {
            _logger.LogDebug(ex, "Map selection fence not remembered: the gesture does not normalise");
        }
    }

    /// <summary>Surfaces an overflow; an operation that changed nothing still has to re-render
    /// because the notice was cleared.</summary>
    private void Report(MapSelectionChange change)
    {
        if (change.Overflow > 0)
            Notice = new MapSelectionNotice(MapSelectionNoticeKind.Overflow, change.Overflow, _selection.MaxCount);

        RaiseStateChanged();
    }

    private void OnSelectionChanged(MapSelectionChange change)
    {
        _statePushes = PushStatesAsync(_statePushes, change);
        RaiseStateChanged();
    }

    /// <summary>Runs after <paramref name="previous"/>; never faults, so one failed push cannot
    /// block the ones queued behind it.</summary>
    private async Task PushStatesAsync(Task previous, MapSelectionChange change)
    {
        await previous;
        if (_disposed) return;

        try
        {
            await _interop.SetFeatureStatesAsync(_divId, change.Removed, MapFeatureState.Default);
            await _interop.SetFeatureStatesAsync(_divId, change.Added, MapFeatureState.Selected);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not push selection states to map {DivId}", _divId);
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();
}
