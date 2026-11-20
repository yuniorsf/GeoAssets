using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>
/// The kernel as the NeutralOperator host sees it, in process: an <see cref="HttpMessageHandler"/> that answers the caller's
/// identity routes (<c>/api/identity/me</c>, <c>/api/identity/organization-grants/me</c>), the global target ledger
/// (<c>/api/targets/ledger/{vertical}/{order}/reserve|release|commit|usages</c>) and the bootstrap registration PUTs, and keeps
/// every request it received. A reservation takes the feature ids named in the selection's <c>featureIds</c> parameter, so a
/// test decides what a "strategy" selects. Thread-safe: the host calls it from request threads and from its registration loop.
/// </summary>
internal sealed class FakeKernel(IReadOnlyDictionary<string, TestIdentity> identities) : HttpMessageHandler
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;
    private static readonly JsonSerializerOptions _web = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<TargetLedgerEntry>> _ledger = new(StringComparer.Ordinal);

    public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

    /// <summary>Makes the registration PUTs answer 503 while true.</summary>
    public bool RegistrationFails { get; set; }

    /// <summary>What a reservation or preview does.</summary>
    public KernelFault ReserveFault { get; set; }

    /// <summary>What a release does.</summary>
    public KernelFault ReleaseFault { get; set; }

    /// <summary>Makes every <c>/api/identity/me</c> answer 503 while true.</summary>
    public bool IdentityFails { get; set; }

    public IReadOnlyList<RecordedRequest> To(HttpMethod method, string pathContains) =>
        [.. Requests.Where(r => r.Method == method && r.Path.Contains(pathContains, StringComparison.Ordinal))];

    /// <summary>The targets the ledger holds for <paramref name="orderId"/> of <paramref name="verticalId"/>, released ones included.</summary>
    public IReadOnlyList<TargetLedgerEntry> LedgerOf(string orderId)
    {
        lock (_gate)
        {
            return _ledger.TryGetValue(orderId, out var rows) ? [.. rows] : [];
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => string.Join(',', h.Value), StringComparer.OrdinalIgnoreCase);
        var uri = request.RequestUri!;
        var recorded = new RecordedRequest(request.Method, uri.AbsolutePath, uri.Query, headers, body);
        _requests.Enqueue(recorded);

        return Route(recorded);
    }

    private HttpResponseMessage Route(RecordedRequest request)
    {
        var segments = request.Path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (request.Method == HttpMethod.Get && string.Equals(request.Path, "/api/identity/me", StringComparison.Ordinal)) return Me(request);
        if (request.Method == HttpMethod.Get && string.Equals(request.Path, "/api/identity/organization-grants/me", StringComparison.Ordinal)) return Json(HttpStatusCode.OK, "[]");
        if (request.Method == HttpMethod.Get && string.Equals(request.Path, "/api/identity/policies", StringComparison.Ordinal)) return Json(HttpStatusCode.OK, "[]");
        if (request.Method == HttpMethod.Put && segments is ["api", "registry", "verticals", _, "order-types"]) return Registration();
        if (request.Method == HttpMethod.Put && segments is ["api", "identity", "verticals", _, "permissions"]) return Registration();
        if (segments is ["api", "targets", "ledger", var vertical, var order, var operation])
            return Ledger(request, Uri.UnescapeDataString(vertical), Uri.UnescapeDataString(order), operation);

        return Problem(HttpStatusCode.NotFound, "kernel.stub-not-found", $"The stubbed kernel has no route {request.Method} {request.Path}.");
    }

    private HttpResponseMessage Me(RecordedRequest request)
    {
        if (IdentityFails) return Problem(HttpStatusCode.ServiceUnavailable, "kernel.unavailable", "The kernel is down.");

        var token = request.Authorization?.StartsWith("Bearer ", StringComparison.Ordinal) == true ? request.Authorization["Bearer ".Length..] : null;
        if (token is null || !identities.TryGetValue(token, out var identity)) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(
            new
            {
                id = identity.UserId,
                email = $"{token}@example.test",
                displayName = token,
                organizationId = identity.OrganizationId,
                roles = identity.Roles,
                claims = Array.Empty<object>(),
                permissions = identity.Permissions,
            },
            _web));
    }

    private HttpResponseMessage Registration() =>
        RegistrationFails
            ? Problem(HttpStatusCode.ServiceUnavailable, "kernel.unavailable", "The kernel is not ready.")
            : new HttpResponseMessage(HttpStatusCode.NoContent);

    private HttpResponseMessage Ledger(RecordedRequest request, string vertical, string orderId, string operation) => operation switch
    {
        "reserve" when request.Method == HttpMethod.Post => Reserve(request, vertical, orderId),
        "release" when request.Method == HttpMethod.Post => Release(request, orderId),
        "usages" when request.Method == HttpMethod.Get => Usages(request, orderId),
        _ => Problem(HttpStatusCode.NotFound, "kernel.stub-not-found", $"The stubbed ledger has no operation {request.Method} {operation}."),
    };

    private HttpResponseMessage Reserve(RecordedRequest request, string vertical, string orderId)
    {
        switch (ReserveFault)
        {
            case KernelFault.Unreachable: throw new HttpRequestException("The kernel is unreachable.");
            case KernelFault.ServerError: return Problem(HttpStatusCode.ServiceUnavailable, "kernel.unavailable", "The kernel is down.");
            case KernelFault.Conflict:
                return Problem(
                    HttpStatusCode.Conflict, TargetLedger.ConflictProblemCode, "Targets are held by another order.",
                    ("conflicts", new[] { new TargetLedgerConflict("pole-1", "kernel", OrderTargetExclusivity.BlockOnOverlap, null, null) }));
        }

        var json = request.Json;
        var organizationId = json.GetProperty("organizationId").GetGuid();
        var preview = json.GetProperty("preview").GetBoolean();
        var orderTypeId = json.GetProperty("orderTypeId").GetString()!;
        var featureIds = json.TryGetProperty("parameters", out var parameters) && parameters.TryGetProperty("featureIds", out var ids)
            ? ids.EnumerateArray().Select(id => id.GetString()!).ToList()
            : [];
        var actor = json.TryGetProperty("actor", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()! : "actor";

        lock (_gate)
        {
            var rows = _ledger.TryGetValue(orderId, out var existing) ? existing : [];
            var held = rows.Where(r => r.ReleasedAt is null).Select(r => r.Target.FeatureId).ToHashSet(StringComparer.Ordinal);
            var fresh = featureIds.Where(f => !held.Contains(f)).Distinct(StringComparer.Ordinal).Select(f => Target(vertical, orderId, f, organizationId, actor)).ToList();

            if (preview)
            {
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(
                    new TargetSelectionPreview
                    {
                        OrderTypeId = orderTypeId,
                        Matched = featureIds.Count,
                        Eligible = fresh.Count,
                        AlreadyInOrder = featureIds.Count - fresh.Count,
                        TruncatedByLimit = 0,
                        WarningCount = 0,
                        BlockedCount = 0,
                        FirstPage = new PagedResult<ServiceOrderTarget> { Items = fresh, TotalCount = fresh.Count },
                    },
                    _opts));
            }

            rows.AddRange(fresh.Select(t => new TargetLedgerEntry(t, null)));
            _ledger[orderId] = rows;
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(
                new TargetSelectionResult
                {
                    OrderTypeId = orderTypeId,
                    Added = fresh.Count,
                    Matched = featureIds.Count,
                    Eligible = fresh.Count,
                    AlreadyInOrder = featureIds.Count - fresh.Count,
                    TruncatedByLimit = 0,
                    WarningCount = 0,
                    BlockedCount = 0,
                },
                _opts));
        }
    }

    private HttpResponseMessage Release(RecordedRequest request, string orderId)
    {
        switch (ReleaseFault)
        {
            case KernelFault.Unreachable: throw new HttpRequestException("The kernel is unreachable.");
            case KernelFault.ServerError: return Problem(HttpStatusCode.ServiceUnavailable, "kernel.unavailable", "The kernel is down.");
        }

        lock (_gate)
        {
            if (!_ledger.TryGetValue(orderId, out var rows)) return Problem(HttpStatusCode.NotFound, "orders.not-found", "The ledger holds nothing of the order.");

            var released = new List<object>();
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].ReleasedAt is not null) continue;
                released.Add(new { targetId = rows[i].Target.Id, featureId = rows[i].Target.FeatureId });
                rows[i] = rows[i] with { ReleasedAt = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc) };
            }

            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(
                new { verticalId = "neutral-operator", orderId, changed = released.Count, targets = released }, _opts));
        }
    }

    private HttpResponseMessage Usages(RecordedRequest request, string orderId)
    {
        var query = System.Web.HttpUtility.ParseQueryString(request.Query);
        var skip = int.Parse(query["skip"] ?? "0", CultureInfo.InvariantCulture);
        var take = int.Parse(query["take"] ?? "50", CultureInfo.InvariantCulture);

        lock (_gate)
        {
            if (!_ledger.TryGetValue(orderId, out var rows)) return Problem(HttpStatusCode.NotFound, "orders.not-found", "The ledger holds nothing of the order.");

            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(
                new { items = rows.Skip(skip).Take(take).ToList(), skip, take, totalCount = rows.Count }, _opts));
        }
    }

    private static ServiceOrderTarget Target(string vertical, string orderId, string featureId, Guid organizationId, string actor) =>
        new ServiceOrderTargetBuilder()
            .WithId(TargetLedger.TargetId(vertical, orderId, featureId))
            .WithOrderId(orderId)
            .WithFeatureId(featureId)
            .WithAlias(featureId)
            .WithActivityIds(["relocate-attachment"])
            .WithOrganizationId(organizationId)
            .WithAddedBy(actor)
            .Build() with { VerticalId = vertical };

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code, string detail, params (string Name, object Value)[] extra)
    {
        var members = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["type"] = $"urn:geoassets:problem:{code}",
            ["title"] = detail,
            ["status"] = (int)status,
            ["code"] = code,
            ["detail"] = detail,
        };
        foreach (var (name, value) in extra) members[name] = value;

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(members, _opts), Encoding.UTF8, "application/problem+json"),
        };
    }
}
