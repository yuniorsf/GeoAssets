using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// How the service-order routes answer a caller the organization gate refuses (XD01-332), and
/// how the read routes — which evaluate no rule — still keep another organization's orders to
/// themselves.
///
/// <para><b>404, not 403, for a caller outside the order's organization.</b> A caller that
/// fails the gate gets exactly the response an unknown order id gets, so a refusal never
/// confirms that an id belongs to another tenant's order. A caller that passes the gate and is
/// then refused by the rule chain (a member of the order's organization who lacks the role, a
/// grantee, a dispatch recipient) keeps the 403 with the engine's reason it always got.</para>
/// </summary>
public static class ServiceOrderRouteAccess
{
    /// <summary>The response for a verdict that did not allow the action.</summary>
    public static IResult Refusal(RuleEvaluationResult verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return verdict.IsOutsideOrganization
            ? Results.NotFound()
            : Results.Json(new { reason = verdict.Reason }, statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// The response for a service-order requirement that failed (XD01-364): the verdict's answer when the rule chain
    /// refused, 403 with the handler's reason otherwise.
    /// </summary>
    public static IResult Refusal(AuthorizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var reason = result.Failure?.FailureReasons.OfType<ServiceOrderAuthorizationFailureReason>().FirstOrDefault();
        if (reason?.Verdict is { } verdict) return Refusal(verdict);

        return Results.Json(
            new { reason = reason?.Message ?? "Not authorized." }, statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// The order with this id when the caller passes the organization gate on it; null when it does not exist or the caller is
    /// outside its organization — the two are deliberately indistinguishable.
    /// <c>GetByIdAsync</c> ignores the tenant filter of the list routes, so this is the only
    /// thing between a caller and any order it can name.
    /// </summary>
    public static async Task<IServiceOrder?> FindReadableAsync(
        string id, IServiceOrderReader orders, ServiceOrderRules rules,
        ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var order = await orders.GetByIdAsync(id, ct).ConfigureAwait(false);
        return await KeepIfReadableAsync(order, rules, principalFactory, ct).ConfigureAwait(false);
    }

    /// <summary><paramref name="order"/> when the caller passes the organization gate on it, null otherwise.</summary>
    public static async Task<IServiceOrder?> KeepIfReadableAsync(
        IServiceOrder? order, ServiceOrderRules rules, ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(principalFactory);

        if (order is null) return null;

        var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
        return rules.PassesOrganizationGate(principal, order) ? order : null;
    }

    /// <summary>
    /// The orders of a list the caller passes the organization gate on. The store already
    /// narrows a list to the caller's tenant; this holds whatever the store is, and is what
    /// keeps a list empty of other organizations' orders if that narrowing is ever absent.
    /// </summary>
    public static async Task<IReadOnlyList<IServiceOrder>> KeepReadableAsync(
        IReadOnlyList<IServiceOrder> orders, ServiceOrderRules rules,
        ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(principalFactory);

        if (orders.Count == 0) return orders;

        var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
        return [.. orders.Where(o => rules.PassesOrganizationGate(principal, o))];
    }

    /// <summary>The caller's organization, <see cref="Guid.Empty"/> when it has none.</summary>
    public static Guid OrganizationOf(WorkflowPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return Guid.TryParse(principal.OrganizationId, out var organizationId) ? organizationId : Guid.Empty;
    }
}
