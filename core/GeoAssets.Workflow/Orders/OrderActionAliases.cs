namespace GeoAssets.Workflow.Orders;

/// <summary>
/// The stable numbers of the actions <see cref="OrderActionType"/> enumerated (XD01-366) — the compatibility table between
/// the string vocabulary of <see cref="OrderAction"/> and every number already stored in the action log and the order-type
/// tables or sent as JSON. The numbers are the enumeration's own and are never renumbered or reused; an action declared
/// after the enumeration was closed has no number and travels as its code.
/// </summary>
public static class OrderActionAliases
{
    // Index = the historical number. Closed: the enumeration no longer grows, so neither does this table.
    private static readonly string[] Codes =
    [
        "View",            // 0
        "Approve",         // 1
        "Reject",          // 2
        "Assign",          // 3
        "Accept",          // 4
        "Dispatch",        // 5
        "Execute",         // 6
        "Complete",        // 7
        "Cancel",          // 8
        "Annotate",        // 9
        "Calculate",       // 10 — IP billing (XD01-176), now IpBillingActions.Calculate
        "Validate",        // 11 — IP billing (XD01-176), now IpBillingActions.Validate
        "FlagClandestine", // 12 — ISP (XD01-263), now NeutralOperatorActions.FlagClandestine
        "Dispute",         // 13 — ISP (XD01-269), now NeutralOperatorActions.Dispute
    ];

    private static readonly Dictionary<string, int> NumbersByCode =
        Codes.Select((code, number) => (code, number)).ToDictionary(x => x.code, x => x.number, StringComparer.Ordinal);

    /// <summary>Every historical number with the action it names, in number order.</summary>
    public static IReadOnlyList<(int Number, OrderAction Action)> All { get; } =
        [.. Codes.Select((code, number) => (number, new OrderAction(code)))];

    /// <summary>The action stored as <paramref name="number"/>; throws for a number no action ever had.</summary>
    public static OrderAction FromNumber(int number) =>
        TryFromNumber(number, out var action)
            ? action
            : throw new ArgumentOutOfRangeException(nameof(number), number, "No order action has this number.");

    /// <summary>The action stored as <paramref name="number"/>, if any action ever had it.</summary>
    public static bool TryFromNumber(int number, out OrderAction action)
    {
        if (number >= 0 && number < Codes.Length)
        {
            action = new OrderAction(Codes[number]);
            return true;
        }

        action = default;
        return false;
    }

    /// <summary>The historical number of <paramref name="action"/>; null for an action that never had one.</summary>
    public static int? NumberOf(OrderAction action) =>
        !action.IsEmpty && NumbersByCode.TryGetValue(action.Code, out var number) ? number : null;
}
