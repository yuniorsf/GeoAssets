using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// Configuration surface for <see cref="WorkflowServiceExtensions.AddServiceOrderRules"/>.
/// Mirrors <see cref="ServiceOrderRules"/>'s constructor parameters so role-based grants —
/// including any role used to scope what an AI agent actor may do — are supplied as data,
/// not by editing engine code.
/// </summary>
public sealed class ServiceOrderRulesOptions
{
    /// <summary>
    /// Overrides the built-in role → allowed-actions defaults. Leave empty to keep them.
    /// </summary>
    public Dictionary<string, IReadOnlySet<OrderActionType>> RoleGrants { get; } = [];

    /// <summary>
    /// Overrides the built-in unrestricted-role defaults (role names granted every
    /// action). Leave empty to keep the default ("Administrator").
    /// </summary>
    public HashSet<string> UnrestrictedRoles { get; } = [];

    /// <summary>
    /// Actions a dispatch recipient (direct/group/org) may additionally perform when they
    /// also hold the paired role — evaluated by the built-in dispatch-recipient rule. Unlike
    /// <see cref="RoleGrants"/> (global, applies to every order), a grant here only applies
    /// to orders actually dispatched to the principal. Leave empty to keep the default
    /// (none — a recipient without a configured role grant still only gets
    /// View/Annotate/Accept).
    /// </summary>
    public Dictionary<string, IReadOnlySet<OrderActionType>> RecipientRoleGrants { get; } = [];
}
