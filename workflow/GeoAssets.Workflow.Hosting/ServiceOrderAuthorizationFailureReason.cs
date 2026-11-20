using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Why a service-order requirement failed (XD01-364): the rule chain's verdict when there is one, so
/// <see cref="ServiceOrderRouteAccess.Refusal(AuthorizationResult)"/> answers exactly as the workflow routes do — 404 for a
/// caller outside the order's organization, 403 with the engine's reason otherwise.
/// </summary>
public sealed class ServiceOrderAuthorizationFailureReason : AuthorizationFailureReason
{
    public ServiceOrderAuthorizationFailureReason(IAuthorizationHandler handler, RuleEvaluationResult verdict)
        : base(handler, verdict?.Reason ?? throw new ArgumentNullException(nameof(verdict))) => Verdict = verdict;

    public ServiceOrderAuthorizationFailureReason(IAuthorizationHandler handler, string message)
        : base(handler, message)
    {
    }

    /// <summary>The rule chain's refusal; null for a refusal that has none (a creation the type's policies do not admit).</summary>
    public RuleEvaluationResult? Verdict { get; }
}
