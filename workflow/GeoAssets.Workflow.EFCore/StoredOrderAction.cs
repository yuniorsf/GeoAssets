using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// How an <see cref="OrderAction"/> is stored during the transition off the closed enumeration (XD01-366): every action
/// column has a text code column next to its number column. Writes fill both — the number when the action has a historical
/// one (<see cref="OrderActionAliases"/>), so a reader or a row-level security function that only knows numbers keeps
/// working — and reads prefer the code, falling back to the number for a row written before the code column existed.
/// </summary>
internal static class StoredOrderAction
{
    /// <summary>Longest code a column holds.</summary>
    public const int CodeMaxLength = 64;

    public static int? NumberOf(OrderAction action) => OrderActionAliases.NumberOf(action);

    public static string CodeOf(OrderAction action) => action.Code;

    /// <summary>The action of a stored row; throws for a row with neither a code nor a known number.</summary>
    public static OrderAction Read(int? number, string? code) =>
        TryRead(number, code, out var action)
            ? action
            : throw new InvalidOperationException($"A stored order action has neither a code nor a known number ({number}).");

    /// <summary>The action of a stored row, if its code or number names one.</summary>
    public static bool TryRead(int? number, string? code, out OrderAction action)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            action = new OrderAction(code);
            return true;
        }

        if (number is { } n) return OrderActionAliases.TryFromNumber(n, out action);

        action = default;
        return false;
    }
}
