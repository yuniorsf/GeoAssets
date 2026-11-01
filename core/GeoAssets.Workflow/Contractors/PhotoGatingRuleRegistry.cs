namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// In-memory catalogue of <see cref="PhotoGatingRule"/>s, keyed by BoQ item code — same
/// register/find shape as <see cref="GeoAssets.Workflow.Orders.OrderTypeRegistry"/>. Populate at
/// startup from whatever configuration source a host chooses (static list, database, etc.) —
/// this registry only owns the lookup, not where the rules come from.
/// </summary>
public sealed class PhotoGatingRuleRegistry
{
    private readonly Dictionary<string, PhotoGatingRule> _rules = new(StringComparer.OrdinalIgnoreCase);

    public void Register(PhotoGatingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules[rule.ItemCode] = rule;
    }

    public PhotoGatingRule? Find(string itemCode) => _rules.GetValueOrDefault(itemCode);
}
