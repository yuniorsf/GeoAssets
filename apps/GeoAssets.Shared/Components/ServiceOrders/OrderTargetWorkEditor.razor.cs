using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

/// <summary>
/// Records the work on one order target (XD01-313): the activities asked on it (from the order
/// type's catalog), its status (one lifecycle step per save) and a note. Saves through
/// <see cref="IServiceOrderTargetWriter.UpdateAsync"/> with the version the target was read at;
/// when someone else changed it first, the editor shows the stored values and says so instead
/// of overwriting them. The rules live in <see cref="OrderTargetWorkDraft"/> and
/// <see cref="OrderTargetWorkSession"/>; this class only wires them to the markup.
/// </summary>
public partial class OrderTargetWorkEditor
{
    private readonly string _idPrefix = $"work-{Guid.NewGuid():N}";
    private readonly CancellationTokenSource _lifetime = new();

    private string? _loadedKey;
    private OrderTargetWorkSession? _session;

    /// <summary>The target as last read.</summary>
    [Parameter, EditorRequired] public ServiceOrderTarget Target { get; set; } = null!;

    /// <summary>The order type's activities; empty when it defines none.</summary>
    [Parameter] public IReadOnlyList<OrderTargetActivity>? ActivityCatalog { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Raised whenever the stored target is known to have changed: after a save, or after a conflict read back someone else's change.</summary>
    [Parameter] public EventCallback<OrderTargetWorkResult> OnStored { get; set; }

    private string TargetLabel => Target.Alias ?? Target.FeatureId;

    protected override void OnParametersSet()
    {
        var key = $"{Target.OrderId}\n{Target.Id}";
        if (string.Equals(key, _loadedKey, StringComparison.Ordinal)) return;

        _loadedKey = key;
        _session = new OrderTargetWorkSession(Reader, Writer, Target, ActivityCatalog ?? []);
    }

    private async Task SaveAsync()
    {
        if (_session is null) return;

        try
        {
            if (await _session.SaveAsync(_lifetime.Token) is { } result)
                await OnStored.InvokeAsync(result);
        }
        catch (OperationCanceledException)
        {
            // The editor went away while saving.
        }
    }

    private Task CloseAsync() => OnClose.InvokeAsync();

    private string Id(string suffix) => $"{_idPrefix}-{suffix}";

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
