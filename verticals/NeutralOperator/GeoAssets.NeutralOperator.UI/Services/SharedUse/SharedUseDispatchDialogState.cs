using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.NeutralOperator.UI.Services.SharedUse;

/// <summary>
/// The decisions of the shared-use dispatch dialog (XD01-316), free of any rendering: which
/// dispatch dialog an order gets, whether the confirm button is live, and what the dialog must
/// warn about before the user confirms.
/// </summary>
public static class SharedUseDispatchDialogState
{
    /// <summary>Recipients, and skipped attachments, shown per page of the dialog's tables.</summary>
    public const int PageSize = 10;

    /// <summary>
    /// Whether an order of <paramref name="orderType"/> is dispatched as a fan-out to its poles'
    /// occupants — and so gets this dialog — rather than to one named recipient.
    /// </summary>
    public static bool IsFanOut(OrderType? orderType) => orderType?.TargetPolicy?.FanOut is not null;

    /// <summary>
    /// Whether confirming would do anything and is allowed right now: a plan is loaded, nothing
    /// blocks it, it has at least one occupant without an order yet, and no request is running.
    /// </summary>
    public static bool CanConfirm(SharedUseDispatchPreview? preview, bool busy) =>
        !busy && preview is { CanDispatch: true, BlockReason: null, NewRecipientCount: > 0 };

    /// <summary>
    /// True when the plan is dispatchable in principle and every occupant already has its order:
    /// not an error, just nothing left to do.
    /// </summary>
    public static bool HasNothingNew(SharedUseDispatchPreview? preview) =>
        preview is { BlockReason: null, NewRecipientCount: 0 } && preview.Recipients.TotalCount > 0;

    /// <summary>
    /// What a dispatchable plan leaves out, as the user should hear it before confirming, in a
    /// fixed order: poles another order blocks, poles with nobody to notify, attachments that get
    /// no order. Empty when the plan leaves nothing out.
    /// </summary>
    public static IReadOnlyList<SharedUseDispatchCaveat> Caveats(SharedUseDispatchPreview? preview)
    {
        if (preview is null) return [];

        var caveats = new List<SharedUseDispatchCaveat>(3);
        if (preview.BlockedTargetCount > 0)
            caveats.Add(new SharedUseDispatchCaveat("sharedUse.caveat.blocked", preview.BlockedTargetCount));
        if (preview.UnnotifiablePoleCount > 0)
            caveats.Add(new SharedUseDispatchCaveat("sharedUse.caveat.unnotifiable", preview.UnnotifiablePoleCount));
        if (preview.SkippedEntryCount > 0)
            caveats.Add(new SharedUseDispatchCaveat("sharedUse.caveat.skipped", preview.SkippedEntryCount));

        return caveats;
    }

    /// <summary>Every key <see cref="Caveats"/> can return.</summary>
    public static IReadOnlyList<string> CaveatKeys { get; } =
        ["sharedUse.caveat.blocked", "sharedUse.caveat.unnotifiable", "sharedUse.caveat.skipped"];
}
