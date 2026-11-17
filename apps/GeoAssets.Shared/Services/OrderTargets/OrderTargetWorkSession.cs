using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// The save logic behind the per-target work editor (XD01-313), over the target ports so every
/// branch is testable without rendering.
///
/// <list type="bullet">
/// <item><description>
/// A save sends the draft with the <see cref="ServiceOrderTarget.Version"/> the target was read
/// at. On success the draft starts over from the saved target at the next version.
/// </description></item>
/// <item><description>
/// When someone else changed the target first (<see cref="ServiceOrderConcurrencyException"/>),
/// nothing is overwritten: the target is read again, the draft starts over from what is stored
/// now, and <see cref="ErrorKey"/> says so. If the target is gone the draft is dropped; if it
/// cannot be read the draft stays, and saving again re-checks the version.
/// </description></item>
/// <item><description>
/// A save while another is running sends nothing. Only a cancellation the caller asked for
/// propagates; every other failure becomes an <see cref="ErrorKey"/>.
/// </description></item>
/// </list>
/// </summary>
public sealed class OrderTargetWorkSession
{
    private readonly IServiceOrderTargetReader _reader;
    private readonly IServiceOrderTargetWriter _writer;
    private readonly IReadOnlyList<OrderTargetActivity> _catalog;

    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public OrderTargetWorkSession(
        IServiceOrderTargetReader reader,
        IServiceOrderTargetWriter writer,
        ServiceOrderTarget target,
        IReadOnlyList<OrderTargetActivity> catalog)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(catalog);

        (_reader, _writer, _catalog) = (reader, writer, catalog);
        Draft = new OrderTargetWorkDraft(target, catalog);
    }

    /// <summary>The working copy; null once the target turned out to be gone.</summary>
    public OrderTargetWorkDraft? Draft { get; private set; }

    /// <summary>Whether a save is running.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>The message key of the last save's failure, if it failed.</summary>
    public string? ErrorKey { get; private set; }

    /// <summary>Whether a save can be started now.</summary>
    public bool CanSave => !IsBusy && Draft is { CanSave: true };

    /// <summary>
    /// Saves the draft. Returns the target as stored afterwards — the saved one, or, after a
    /// conflict, the one someone else stored — or null when nothing was saved and nothing newer was read.
    /// </summary>
    public async Task<OrderTargetWorkResult?> SaveAsync(CancellationToken ct)
    {
        if (!CanSave) return null;

        var draft = Draft!;
        IsBusy = true;
        ErrorKey = null;
        try
        {
            var edited = draft.Build();
            await _writer.UpdateAsync(edited, ct);

            var saved = edited with { Version = edited.Version + 1 };
            Draft = new OrderTargetWorkDraft(saved, _catalog);
            return new OrderTargetWorkResult(saved, Saved: true);
        }
        catch (ServiceOrderConcurrencyException)
        {
            return await ReloadAfterConflictAsync(draft.Original, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorKey = OrderTargetWorkMessages.ErrorKey(ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<OrderTargetWorkResult?> ReloadAfterConflictAsync(ServiceOrderTarget original, CancellationToken ct)
    {
        try
        {
            var current = await _reader.GetByIdAsync(original.OrderId, original.Id, ct);
            if (current is null)
            {
                (Draft, ErrorKey) = (null, OrderTargetWorkMessages.NotFoundKey);
                return null;
            }

            (Draft, ErrorKey) = (new OrderTargetWorkDraft(current, _catalog), OrderTargetWorkMessages.ConcurrentKey);
            return new OrderTargetWorkResult(current, Saved: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ErrorKey = OrderTargetWorkMessages.ConcurrentReloadFailedKey;
            return null;
        }
    }
}
