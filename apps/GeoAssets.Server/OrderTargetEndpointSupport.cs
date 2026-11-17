using System.Globalization;
using System.Text.Json;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Server;

/// <summary>
/// What every order-target route does before its own work (XD01-308): read a JSON body, read
/// and bound the paging values, and decide whether the caller may touch the order at all.
/// The caller is always the <see cref="WorkflowPrincipal"/> built from the token by
/// <see cref="ServerWorkflowPrincipalFactory"/>; nothing in a URL or body names it.
/// </summary>
internal static class OrderTargetEndpointSupport
{
    /// <summary>The most ids one usages or remove request may carry.</summary>
    public const int MaxIdsPerRequest = 500;

    /// <summary>An order the caller was allowed on, or the problem to return instead.</summary>
    internal readonly record struct OrderAccess(IServiceOrder? Order, WorkflowPrincipal? Principal, IResult? Problem);

    /// <summary>
    /// Loads the order and checks the caller may read it: a member of the order's own
    /// organization (what the tenant filter of the order list already shows them), or anyone
    /// the order rules grant <see cref="OrderActionType.View"/> — which is how a
    /// cross-organization grantee or a dispatch recipient gets in. The target store itself
    /// checks nothing. A caller the organization gate refuses (XD01-332) gets the same
    /// <c>orders.not-found</c> as for an order that does not exist.
    /// </summary>
    public static async Task<OrderAccess> ForReadAsync(
        string orderId, IServiceOrderRepository orders, ServiceOrderRules rules,
        ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(orderId, ct).ConfigureAwait(false);
        if (order is null) return new OrderAccess(null, null, WorkflowProblemResults.OrderNotFound(orderId));

        var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
        if (IsMemberOfOwningOrganization(principal, order) && rules.PassesOrganizationGate(principal, order))
            return new OrderAccess(order, principal, null);

        var verdict = rules.Evaluate(principal, OrderActionType.View, order);
        return verdict.Allowed
            ? new OrderAccess(order, principal, null)
            : new OrderAccess(null, null, WorkflowProblemResults.Refused(orderId, verdict));
    }

    /// <summary>
    /// Loads the order and checks the order rules grant the caller <see cref="OrderActionType.Annotate"/>;
    /// refused as <see cref="ForReadAsync"/> refuses.
    /// </summary>
    public static async Task<OrderAccess> ForEditAsync(
        string orderId, IServiceOrderRepository orders, ServiceOrderRules rules,
        ServerWorkflowPrincipalFactory principalFactory, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(orderId, ct).ConfigureAwait(false);
        if (order is null) return new OrderAccess(null, null, WorkflowProblemResults.OrderNotFound(orderId));

        var principal = await principalFactory.CreateAsync(ct).ConfigureAwait(false);
        var verdict = rules.Evaluate(principal, OrderActionType.Annotate, order);
        if (!verdict.Allowed)
            return new OrderAccess(null, null, WorkflowProblemResults.Refused(orderId, verdict));

        // XD01-316: the rules let a recipient of the order's dispatch annotate the order; its
        // targets stay the owning organization's to change.
        return rules.IsDispatchRecipientOnly(principal, order)
            ? new OrderAccess(null, null, WorkflowProblemResults.Denied(ServiceOrderRules.RecipientTargetsAreReadOnlyReason))
            : new OrderAccess(order, principal, null);
    }

    /// <summary>The caller's organization, <see cref="Guid.Empty"/> when the token carries none — as <see cref="HttpTenantContext"/> reads it.</summary>
    public static Guid OrganizationOf(WorkflowPrincipal principal) =>
        Guid.TryParse(principal.OrganizationId, out var organizationId) ? organizationId : Guid.Empty;

    private static bool IsMemberOfOwningOrganization(WorkflowPrincipal principal, IServiceOrder order) =>
        !string.IsNullOrEmpty(principal.UserId) && OrganizationOf(principal) == order.OrganizationId;

    /// <summary>
    /// Reads <c>page</c> (default 1) and <c>pageSize</c> (default 50, at most 200). Returns the
    /// problem to send when either is not a whole number in range, or addresses an offset
    /// beyond what a store can skip; null when both are usable.
    /// </summary>
    public static IResult? ReadPaging(string? pageText, string? pageSizeText, out int page, out int pageSize)
    {
        page = 1;
        pageSize = OrderTargetQuery.DefaultPageSize;

        if (pageText is not null && !int.TryParse(pageText, NumberStyles.Integer, CultureInfo.InvariantCulture, out page))
            return WorkflowProblemResults.PagingOutOfRange("page must be a whole number.");
        if (pageSizeText is not null && !int.TryParse(pageSizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out pageSize))
            return WorkflowProblemResults.PagingOutOfRange("pageSize must be a whole number.");

        return CheckPaging(page, pageSize);
    }

    /// <summary>The same bounds as <see cref="ReadPaging"/>, for paging values that arrived in a body.</summary>
    public static IResult? CheckPaging(int page, int pageSize)
    {
        if (page < 1)
            return WorkflowProblemResults.PagingOutOfRange("page must be 1 or greater.");
        if (pageSize is < 1 or > OrderTargetQuery.MaxPageSize)
            return WorkflowProblemResults.PagingOutOfRange($"pageSize must be between 1 and {OrderTargetQuery.MaxPageSize}.");
        if ((long)(page - 1) * pageSize > int.MaxValue)
            return WorkflowProblemResults.PagingOutOfRange("page is beyond the last addressable page.");

        return null;
    }

    /// <summary>
    /// Reads the request body as a JSON object and binds it to <typeparamref name="T"/>. A
    /// missing, malformed or non-object body yields a <c>validation.failed</c> problem.
    /// <c>Raw</c> lets a route tell an absent member from a null one.
    /// </summary>
    public static async Task<(T? Body, JsonElement Raw, IResult? Problem)> ReadBodyAsync<T>(
        HttpRequest request, JsonSerializerOptions options, CancellationToken ct)
        where T : class
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, default, ct).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return (null, default, WorkflowProblemResults.Validation("The request body must be a JSON object."));

            var raw = document.RootElement.Clone();
            var body = raw.Deserialize<T>(options);
            return body is null
                ? (null, default, WorkflowProblemResults.Validation("The request body must be a JSON object."))
                : (body, raw, null);
        }
        catch (JsonException)
        {
            return (null, default, WorkflowProblemResults.Validation("The request body is not valid JSON for this route."));
        }
    }

    /// <summary>
    /// Checks a list of ids carried in a body: present, 1 to <see cref="MaxIdsPerRequest"/>
    /// entries, none blank. Returns the problem to send, or null when the list is usable.
    /// </summary>
    public static IResult? CheckIds(IReadOnlyList<string>? ids, string memberName)
    {
        if (ids is null || ids.Count == 0)
            return WorkflowProblemResults.Validation($"{memberName} must list at least one id.");
        if (ids.Count > MaxIdsPerRequest)
            return WorkflowProblemResults.Validation($"{memberName} must list at most {MaxIdsPerRequest} ids.");
        if (ids.Any(string.IsNullOrWhiteSpace))
            return WorkflowProblemResults.Validation($"{memberName} must not contain a blank id.");

        return null;
    }
}
