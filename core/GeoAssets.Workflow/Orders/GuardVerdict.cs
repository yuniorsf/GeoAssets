namespace GeoAssets.Workflow.Orders;

/// <summary>An <see cref="ITransitionGuard"/>'s answer: allowed, or denied with a stable <see cref="Code"/> and a reason.</summary>
public sealed record GuardVerdict
{
    private GuardVerdict(bool allowed, string? code, string? reason) => (Allowed, Code, Reason) = (allowed, code, reason);

    public bool Allowed { get; }

    /// <summary>Machine-readable code of a denial, e.g. <c>fieldwork.checklist-unsigned</c>.</summary>
    public string? Code { get; }

    public string? Reason { get; }

    public static GuardVerdict Allow { get; } = new(true, null, null);

    public static GuardVerdict Deny(string code, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(false, code, reason);
    }
}
