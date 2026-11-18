using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Orders;

/// <summary>
/// The result of an order action, independent of any transport (XD01-360): the generic action route maps it to the same
/// answers the default append gives, plus a handler's own, module-specific <see cref="OrderActionOutcomeKind.Problem"/>.
/// </summary>
public sealed record OrderActionOutcome
{
    private OrderActionOutcome(OrderActionOutcomeKind kind) => Kind = kind;

    public OrderActionOutcomeKind Kind { get; }

    /// <summary>The rule chain's refusal, for <see cref="OrderActionOutcomeKind.Denied"/>.</summary>
    public RuleEvaluationResult? Verdict { get; private init; }

    /// <summary>The status the order is in, for <see cref="OrderActionOutcomeKind.InvalidTransition"/>.</summary>
    public string? From { get; private init; }

    /// <summary>The status requested, for <see cref="OrderActionOutcomeKind.InvalidTransition"/>.</summary>
    public string? To { get; private init; }

    /// <summary>HTTP-style status code of a <see cref="OrderActionOutcomeKind.Problem"/>.</summary>
    public int StatusCode { get; private init; }

    /// <summary>Stable machine-readable code of a <see cref="OrderActionOutcomeKind.Problem"/>.</summary>
    public string? Code { get; private init; }

    public string? Title { get; private init; }

    public string? Detail { get; private init; }

    /// <summary>Extra members of a <see cref="OrderActionOutcomeKind.Problem"/>'s body.</summary>
    public IReadOnlyList<(string Name, object? Value)> Extensions { get; private init; } = [];

    public static OrderActionOutcome Applied { get; } = new(OrderActionOutcomeKind.Applied);

    public static OrderActionOutcome NotFound { get; } = new(OrderActionOutcomeKind.NotFound);

    public static OrderActionOutcome ConcurrentModification { get; } = new(OrderActionOutcomeKind.ConcurrentModification);

    public static OrderActionOutcome Denied(RuleEvaluationResult verdict) =>
        new(OrderActionOutcomeKind.Denied) { Verdict = verdict ?? throw new ArgumentNullException(nameof(verdict)) };

    public static OrderActionOutcome InvalidTransition(string? from, string? to) =>
        new(OrderActionOutcomeKind.InvalidTransition) { From = from, To = to };

    public static OrderActionOutcome Problem(
        int statusCode, string code, string title, string? detail, params (string Name, object? Value)[] extensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new(OrderActionOutcomeKind.Problem)
        {
            StatusCode = statusCode,
            Code = code,
            Title = title,
            Detail = detail,
            Extensions = extensions ?? [],
        };
    }
}
