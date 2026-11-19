using System.Text.Json.Serialization;

namespace GeoAssets.Workflow.Orders;

/// <summary>
/// An action on a service order, identified by a string <see cref="Code"/> (XD01-366, ADR-001 smell D) — an open vocabulary.
/// The engine knows only the generic verbs below; a vertical declares its own verbs as <see cref="OrderAction"/> constants in
/// its own module (e.g. <c>IpBillingActions.Calculate</c>), with no change to this assembly, the wire format or the
/// database.
///
/// <para><b>Compatibility.</b> The closed <see cref="OrderActionType"/> enumeration this replaces is persisted as a number in
/// the action log and the order-type tables and exchanged as a number in JSON. Every one of its values keeps that number
/// (<see cref="OrderActionAliases"/>), an <see cref="OrderActionType"/> converts implicitly, and JSON reads a number or a
/// string and writes the number whenever the action has one — so what is stored or sent today reads identically.</para>
/// </summary>
[JsonConverter(typeof(OrderActionJsonConverter))]
public readonly record struct OrderAction
{
    private readonly string? _code;

    public OrderAction(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _code = code;
    }

    /// <summary>The action's code, compared ordinally; empty for <c>default</c>.</summary>
    public string Code => _code ?? string.Empty;

    /// <summary>Whether this is <c>default(OrderAction)</c>, which names no action.</summary>
    public bool IsEmpty => _code is null;

    /// <summary>Read/open the order.</summary>
    public static OrderAction View { get; } = new("View");

    /// <summary>Approve the order to move it to a next stage.</summary>
    public static OrderAction Approve { get; } = new("Approve");

    /// <summary>Reject the order, returning it or closing it.</summary>
    public static OrderAction Reject { get; } = new("Reject");

    /// <summary>Assign the order to a specific user.</summary>
    public static OrderAction Assign { get; } = new("Assign");

    /// <summary>Claim an order dispatched to a group, organization or user.</summary>
    public static OrderAction Accept { get; } = new("Accept");

    /// <summary>Dispatch the order to a user, group or organization.</summary>
    public static OrderAction Dispatch { get; } = new("Dispatch");

    /// <summary>Execute field work recorded on the order.</summary>
    public static OrderAction Execute { get; } = new("Execute");

    /// <summary>Mark the order as completed.</summary>
    public static OrderAction Complete { get; } = new("Complete");

    /// <summary>Cancel the order.</summary>
    public static OrderAction Cancel { get; } = new("Cancel");

    /// <summary>Add comments or attachments to the order.</summary>
    public static OrderAction Annotate { get; } = new("Annotate");

    /// <summary>The generic verbs the engine itself defines.</summary>
    public static IReadOnlyList<OrderAction> Generic { get; } =
        [View, Approve, Reject, Assign, Accept, Dispatch, Execute, Complete, Cancel, Annotate];

    /// <summary>The action an <see cref="OrderActionType"/> value names — the one with the same historical number.</summary>
    public static implicit operator OrderAction(OrderActionType type) => OrderActionAliases.FromNumber((int)type);

    /// <summary>Named alternative to the implicit conversion.</summary>
    public static OrderAction FromOrderActionType(OrderActionType type) => type;

    public override string ToString() => Code;
}
