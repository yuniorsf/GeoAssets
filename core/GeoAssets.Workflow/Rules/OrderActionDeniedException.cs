using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// Thrown when <see cref="ServiceOrderRules"/> refuses the action a service needs to perform on
/// an order on behalf of a principal. <see cref="Evaluation"/> carries the verdict as returned
/// by the rule engine (the action asked for and the engine's reason), so a host can report it
/// without evaluating the rules a second time.
/// </summary>
public sealed class OrderActionDeniedException : Exception
{
    /// <summary>The verdict of the rule engine; null only when created without one.</summary>
    public RuleEvaluationResult? Evaluation { get; }

    /// <summary>The order the action was refused on; null when not known.</summary>
    public string? OrderId { get; }

    public OrderActionDeniedException(string orderId, RuleEvaluationResult evaluation)
        : base(MessageFor(evaluation))
    {
        Evaluation = evaluation;
        OrderId = orderId;
    }

    public OrderActionDeniedException()
    {
    }

    public OrderActionDeniedException(string message) : base(message)
    {
    }

    public OrderActionDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The action that was refused, when a verdict is attached.</summary>
    public OrderAction? Action => Evaluation?.Action;

    private static string MessageFor(RuleEvaluationResult evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        return $"Action '{evaluation.Action}' was denied: {evaluation.Reason}";
    }
}
