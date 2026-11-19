using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Server.Targets;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Server;

/// <summary>
/// The kernel's global target ledger API (XD01-379, ADR-001 §3.4): a remote vertical reserves, releases and commits the
/// targets of its own orders, keyed by <c>(vertical, orderId)</c>, so exclusivity holds across verticals. In-process
/// orders keep their routes (<see cref="ServiceOrderTargetsRestApiExtensions"/>).
///
/// <list type="table">
/// <item><term>POST /api/targets/ledger/{vertical}/{orderId}/reserve</term><description>preview or reserve a selection</description></item>
/// <item><term>POST /api/targets/ledger/{vertical}/{orderId}/release</term><description>release open targets (idempotent)</description></item>
/// <item><term>POST /api/targets/ledger/{vertical}/{orderId}/commit</term><description>mark unfinished targets done (idempotent)</description></item>
/// <item><term>GET  /api/targets/ledger/{vertical}/{orderId}/usages</term><description>the order's targets, paginated</description></item>
/// </list>
///
/// <para><b>Authorization.</b> Machine to machine: every route requires the
/// <see cref="GeoAuthorizationHandler.ServiceCallerAppRole"/> app role — a user token, an administrator's included, gets
/// 403. The caller acts for the vertical in the route and for the organization it names in the body (or, on the read,
/// the query string); no header is trusted for it. A ledger order holding another organization's targets is
/// <c>orders.not-found</c> (404), never 403. <c>kernel</c> is reserved for in-process orders and refused (400).</para>
///
/// <para><b>Errors</b> are the <c>application/problem+json</c> bodies of <see cref="WorkflowProblemResults"/>; a
/// reservation blocked by another order's exclusive hold is 409 <c>targets.conflict</c> with a <c>conflicts</c>
/// array (see <see cref="TargetLedgerConflict"/> for what it shows of another organization's order).</para>
/// </summary>
public static class TargetLedgerRestApiExtensions
{
    /// <summary>The default page size of the usages route.</summary>
    public const int DefaultTake = 100;

    /// <summary>The largest page the usages route returns.</summary>
    public const int MaxTake = 1000;

    private const string ActorFallback = "service-caller";
    private const int MaxActorLength = 256;

    public static IEndpointRouteBuilder MapTargetLedgerApi(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var opts = GeoJsonSerializer.Options;
        var group = routes.MapGroup("/api/targets/ledger/{vertical}/{orderId}")
            .RequireAuthorization(policy => policy.RequireRole(GeoAuthorizationHandler.ServiceCallerAppRole));

        group.MapPost("/reserve", (
            string vertical, string orderId, HttpRequest request, TargetLedgerService ledger, BoundedFeatureSelector selector, CancellationToken ct) =>
            ReserveAsync(vertical, orderId, request, new ReserveServices(ledger, selector), opts, ct));

        group.MapPost("/release", (string vertical, string orderId, HttpRequest request, TargetLedgerService ledger, CancellationToken ct) =>
            ChangeAsync(vertical, orderId, request, opts, (key, organizationId, ids, actor) => ledger.ReleaseAsync(key, organizationId, ids, actor, ct), ct));

        group.MapPost("/commit", (string vertical, string orderId, HttpRequest request, TargetLedgerService ledger, CancellationToken ct) =>
            ChangeAsync(vertical, orderId, request, opts, (key, organizationId, ids, actor) => ledger.CommitAsync(key, organizationId, ids, actor, ct), ct));

        group.MapGet("/usages", (string vertical, string orderId, string? organizationId, string? skip, string? take, TargetLedgerService ledger, CancellationToken ct) =>
            UsagesAsync(vertical, orderId, organizationId, skip, take, ledger, opts, ct));

        return routes;
    }

    private static async Task<IResult> ReserveAsync(
        string vertical, string orderId, HttpRequest request, ReserveServices services, JsonSerializerOptions opts, CancellationToken ct)
    {
        var ledger = services.Ledger;
        if (CheckKey(vertical, orderId) is { } keyProblem) return keyProblem;

        var (body, _, bodyProblem) = await OrderTargetEndpointSupport.ReadBodyAsync<TargetLedgerReserveRequest>(request, opts, ct).ConfigureAwait(false);
        if (bodyProblem is not null) return bodyProblem;
        if (CheckReserveBody(body!) is { } validation) return validation;

        var descriptor = ledger.FindOrderType(vertical, body!.OrderTypeId!);
        if (descriptor is null)
        {
            return WorkflowProblemResults.Create(
                StatusCodes.Status404NotFound, WorkflowProblemCodes.OrderTypeNotFound, "The vertical published no such order type.",
                null, ("verticalId", vertical), ("orderTypeId", body.OrderTypeId));
        }

        if (descriptor.TargetPolicy is not { } policy)
        {
            return WorkflowProblemResults.Create(
                StatusCodes.Status422UnprocessableEntity, WorkflowProblemCodes.NoTargetPolicy, "Orders of this type hold no targets.",
                null, ("orderTypeId", body.OrderTypeId));
        }

        var organizationId = body.OrganizationId!.Value;
        var key = new TargetLedgerKey(vertical, orderId);
        if (await ledger.BelongsToAnotherOrganizationAsync(key, organizationId, ct).ConfigureAwait(false))
            return WorkflowProblemResults.OrderNotFound(orderId);

        var order = new TargetLedgerOrder(vertical, orderId, descriptor.OrderTypeId, organizationId, policy);
        var selection = new TargetSelectionRequest(
            orderId, body.StrategyId!, body.Parameters ?? new Dictionary<string, object>(StringComparer.Ordinal),
            body.Page, body.PageSize, body.AcceptTruncation);
        var actor = Actor(body.Actor, request.HttpContext.User);

        try
        {
            return body.Preview
                ? Results.Json(await ledger.PreviewAsync(order, selection, actor, ct).ConfigureAwait(false), opts)
                : Results.Json(await ledger.ReserveAsync(order, selection, actor, ct).ConfigureAwait(false), opts);
        }
        catch (Exception ex) when (ReserveProblem(ex, orderId, selection.StrategyId, services.Selector) is not null)
        {
            return ReserveProblem(ex, orderId, selection.StrategyId, services.Selector)!;
        }
    }

    private static async Task<IResult> ChangeAsync(
        string vertical,
        string orderId,
        HttpRequest request,
        JsonSerializerOptions opts,
        Func<TargetLedgerKey, Guid, IReadOnlyCollection<string>?, string, Task<IReadOnlyList<ServiceOrderTarget>?>> change,
        CancellationToken ct)
    {
        if (CheckKey(vertical, orderId) is { } keyProblem) return keyProblem;

        var (body, _, bodyProblem) = await OrderTargetEndpointSupport.ReadBodyAsync<TargetLedgerChangeRequest>(request, opts, ct).ConfigureAwait(false);
        if (bodyProblem is not null) return bodyProblem;
        if (CheckOrganization(body!.OrganizationId) is { } organizationProblem) return organizationProblem;
        if (body.TargetIds is not null && OrderTargetEndpointSupport.CheckIds(body.TargetIds, "targetIds") is { } idsProblem) return idsProblem;
        if (body.Actor is { Length: > MaxActorLength }) return WorkflowProblemResults.Validation($"actor must be at most {MaxActorLength} characters.");

        var changed = await change(
            new TargetLedgerKey(vertical, orderId), body.OrganizationId!.Value, body.TargetIds, Actor(body.Actor, request.HttpContext.User))
            .ConfigureAwait(false);
        if (changed is null) return WorkflowProblemResults.OrderNotFound(orderId);

        var items = changed.Select(t => new TargetLedgerEventItem(t.Id, t.FeatureId)).ToList();
        return Results.Json(new TargetLedgerChangeResponse(vertical, orderId, items.Count, items), opts);
    }

    private static async Task<IResult> UsagesAsync(
        string vertical, string orderId, string? organizationIdText, string? skipText, string? takeText,
        TargetLedgerService ledger, JsonSerializerOptions opts, CancellationToken ct)
    {
        if (CheckKey(vertical, orderId) is { } keyProblem) return keyProblem;

        Guid? organizationId = Guid.TryParse(organizationIdText, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        if (CheckOrganization(organizationId) is { } organizationProblem) return organizationProblem;
        if (ReadSlice(skipText, takeText, out var skip, out var take) is { } pagingProblem) return pagingProblem;

        var page = await ledger.GetPageAsync(new TargetLedgerKey(vertical, orderId), organizationId!.Value, skip, take, ct).ConfigureAwait(false);
        return page.TotalCount == 0
            ? WorkflowProblemResults.OrderNotFound(orderId)
            : Results.Json(new TargetLedgerUsagesResponse(page.Items, skip, take, page.TotalCount), opts);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    /// <summary>The route's vertical must be a well-formed remote vertical (never <c>kernel</c>) and the order id must fit the ledger.</summary>
    private static IResult? CheckKey(string vertical, string orderId)
    {
        if (TargetLedger.IsKernel(vertical))
            return WorkflowProblemResults.Validation($"Vertical '{TargetLedger.KernelVerticalId}' is reserved for in-process orders; use the service-order target routes.");
        if (!TargetLedger.IsWellFormedVerticalId(vertical))
            return WorkflowProblemResults.Validation($"vertical must be lower-case words of letters and digits joined by hyphens, at most {TargetLedger.MaxVerticalIdLength} characters.");
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > TargetLedger.MaxOrderIdLength)
            return WorkflowProblemResults.Validation($"orderId must be 1 to {TargetLedger.MaxOrderIdLength} characters.");

        return null;
    }

    private static IResult? CheckOrganization(Guid? organizationId) =>
        organizationId is { } id && id != Guid.Empty
            ? null
            : WorkflowProblemResults.Validation("organizationId is required and must be a non-empty GUID.");

    private static IResult? CheckReserveBody(TargetLedgerReserveRequest body)
    {
        if (CheckOrganization(body.OrganizationId) is { } organizationProblem) return organizationProblem;
        if (string.IsNullOrWhiteSpace(body.OrderTypeId)) return WorkflowProblemResults.Validation("orderTypeId is required.");
        if (string.IsNullOrWhiteSpace(body.StrategyId)) return WorkflowProblemResults.Validation("strategyId is required.");
        if (body.Actor is { Length: > MaxActorLength }) return WorkflowProblemResults.Validation($"actor must be at most {MaxActorLength} characters.");

        return OrderTargetEndpointSupport.CheckPaging(body.Page, body.PageSize);
    }

    /// <summary>Reads <c>skip</c> (default 0) and <c>take</c> (default <see cref="DefaultTake"/>, at most <see cref="MaxTake"/>).</summary>
    private static IResult? ReadSlice(string? skipText, string? takeText, out int skip, out int take)
    {
        skip = 0;
        take = DefaultTake;

        if (skipText is not null && !int.TryParse(skipText, NumberStyles.Integer, CultureInfo.InvariantCulture, out skip))
            return WorkflowProblemResults.PagingOutOfRange("skip must be a whole number.");
        if (takeText is not null && !int.TryParse(takeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out take))
            return WorkflowProblemResults.PagingOutOfRange("take must be a whole number.");
        if (skip < 0)
            return WorkflowProblemResults.PagingOutOfRange("skip must be 0 or greater.");
        if (take is < 1 or > MaxTake)
            return WorkflowProblemResults.PagingOutOfRange($"take must be between 1 and {MaxTake}.");

        return null;
    }

    // ── Problems and identity ────────────────────────────────────────────────

    /// <summary>
    /// The problem for a failed preview or reservation, or null when the failure is not the caller's to fix. The order
    /// type and its policy were resolved before the call, so a <see cref="KeyNotFoundException"/> means an unknown
    /// strategy or a missing parameter, and an <see cref="InvalidOperationException"/> other than a conflict means a
    /// strategy that needs an order the kernel does not hold (inheriting from a parent or children).
    /// </summary>
    private static IResult? ReserveProblem(Exception exception, string orderId, string strategyId, BoundedFeatureSelector selector) => exception switch
    {
        TargetLedgerConflictException conflict => WorkflowProblemResults.Create(
            StatusCodes.Status409Conflict, TargetLedger.ConflictProblemCode,
            "Another order holds some of these features with blocking exclusivity.", conflict.Message,
            ("orderId", orderId), ("conflicts", conflict.Conflicts)),
        KeyNotFoundException when !selector.HasStrategy(strategyId) => WorkflowProblemResults.UnknownStrategy(strategyId),
        KeyNotFoundException => WorkflowProblemResults.InvalidSelectionParameters("A required selection parameter is missing."),
        _ when WorkflowProblemResults.FromException(exception, orderId) is { } known => known,
        InvalidOperationException => WorkflowProblemResults.InvalidSelectionParameters(
            "This selection strategy needs an order the kernel holds.", exception.Message),
        _ => null,
    };

    /// <summary>What the reserve route resolves from the request's services.</summary>
    private sealed record ReserveServices(TargetLedgerService Ledger, BoundedFeatureSelector Selector);

    /// <summary>The recorded actor: the body's, else the calling service's identity.</summary>
    private static string Actor(string? actor, ClaimsPrincipal caller)
    {
        if (!string.IsNullOrWhiteSpace(actor)) return actor;

        var identity = caller.FindFirst("azp")?.Value
            ?? caller.FindFirst("appid")?.Value
            ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? caller.FindFirst("sub")?.Value;

        return string.IsNullOrWhiteSpace(identity) ? ActorFallback : identity[..Math.Min(identity.Length, MaxActorLength)];
    }
}
