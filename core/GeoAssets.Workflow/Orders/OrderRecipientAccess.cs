namespace GeoAssets.Workflow.Orders;

/// <summary>
/// What an order type lets the organization an order of that type was dispatched to do with it
/// (XD01-333) — the intent of the dispatch: to inform the recipient, to have it carry out the
/// work on the order's targets, or to hand it the order to edit.
///
/// It applies only to a principal that reaches an order through a dispatch and through nothing
/// else (<see cref="Rules.ServiceOrderRules.IsDispatchRecipientOnly"/>): a member of the order's
/// organization and a cross-organization grantee are never limited by it. It is a ceiling, never
/// a grant: the rule chain still has to allow the action, so a recipient whose role lacks
/// <see cref="OrderActionType.Annotate"/> records no work even on an <see cref="Execute"/> order.
///
/// The numeric values are stored (<c>"OrderTypes"."RecipientAccess"</c>) and compared by the
/// row-level security functions of both <c>ServiceOrderDbContext</c> histories, so they are
/// ordered from least to most access and must never be renumbered.
/// </summary>
public enum OrderRecipientAccess
{
    /// <summary>
    /// The recipient reads the order and its targets, and changes nothing: no field, no action
    /// other than the type's <see cref="OrderType.RecipientActions"/> (XD01-336), no target. The
    /// default for every type that does not say otherwise.
    /// </summary>
    Inform = 0,

    /// <summary>
    /// The recipient also records the work on each target — its activities, status and note. It
    /// changes nothing of the order itself, takes no action on it, and neither adds nor removes
    /// a target.
    /// </summary>
    Execute = 1,

    /// <summary>
    /// The recipient also edits the order — its title, description, priority, schedule and
    /// attributes, through the whole-order update — and takes every action the rule chain allows
    /// it. It still neither adds nor removes a target, and cannot change what the order is
    /// (<see cref="ServiceOrderRecipientEdit.FixedFieldsChanged"/>).
    /// </summary>
    Edit = 2,
}
