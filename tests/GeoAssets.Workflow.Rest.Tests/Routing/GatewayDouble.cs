using System.Net;
using System.Text;
using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rest.Tests.Routing;

/// <summary>
/// The gateway as a descriptor-routed client sees it, in process: one handler answering for every root. Each root keeps the
/// orders it serves; a root can be made to fail; every request is kept as an absolute URI.
/// </summary>
internal sealed class GatewayDouble : HttpMessageHandler
{
    public const string Origin = "http://gw";
    public const string KernelPath = "/api/workflow/";
    public const string VerticalPath = "/api/v/vertical-a/workflow/";

    private readonly Dictionary<string, List<ServiceOrder>> _orders = new(StringComparer.Ordinal) { [KernelPath] = [], [VerticalPath] = [] };
    private readonly HashSet<string> _failing = new(StringComparer.Ordinal);

    public static Uri Kernel { get; } = new($"{Origin}{KernelPath}");

    public static Uri Vertical { get; } = new($"{Origin}{VerticalPath}");

    public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

    /// <summary>The orders root <paramref name="path"/> serves.</summary>
    public GatewayDouble Serves(string path, params ServiceOrder[] orders)
    {
        _orders[path] = [.. orders];
        return this;
    }

    /// <summary>Makes every request of <paramref name="path"/> answer 503.</summary>
    public GatewayDouble Fails(string path)
    {
        _failing.Add(path);
        return this;
    }

    public IEnumerable<Uri> RequestUris => Requests.Select(r => r.Uri);

    public IEnumerable<string> PathsOf(HttpMethod method) => Requests.Where(r => r.Method == method).Select(r => r.Uri.AbsolutePath);

    public static ServiceOrder Order(string id, string orderTypeId = "inspection") => new() { Id = id, OrderTypeId = orderTypeId, Status = ServiceOrderStatus.Draft };

    public static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body, GeoJsonSerializer.Options), Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri!;
        Requests.Add((request.Method, uri, body));

        var path = uri.AbsolutePath;
        var root = _orders.Keys.Concat(_failing).Distinct(StringComparer.Ordinal).FirstOrDefault(r => path.StartsWith(r, StringComparison.Ordinal));
        if (root is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (_failing.Contains(root)) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var rest = path[root.Length..];
        var orders = _orders.GetValueOrDefault(root) ?? [];

        if (request.Method == HttpMethod.Get)
        {
            if (string.Equals(rest, "service-orders", StringComparison.Ordinal)) return Json(HttpStatusCode.OK, orders);
            if (rest.StartsWith("service-orders/by-order-type/", StringComparison.Ordinal))
            {
                var type = Uri.UnescapeDataString(rest["service-orders/by-order-type/".Length..]);
                return Json(HttpStatusCode.OK, orders.Where(o => string.Equals(o.OrderTypeId, type, StringComparison.Ordinal)).ToList());
            }

            if (rest.StartsWith("service-orders/", StringComparison.Ordinal) && !rest["service-orders/".Length..].Contains('/', StringComparison.Ordinal)
                && !string.Equals(rest, "service-orders/roots", StringComparison.Ordinal) && !string.Equals(rest, "service-orders/by-date-range", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(rest["service-orders/".Length..]);
                var found = orders.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));
                return found is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(HttpStatusCode.OK, found);
            }

            if (rest.EndsWith("/children", StringComparison.Ordinal) || rest.StartsWith("service-orders/by-", StringComparison.Ordinal)
                || rest.StartsWith("service-orders/roots", StringComparison.Ordinal) || rest.StartsWith("service-orders/dispatched-to/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, new List<ServiceOrder>());
            }

            if (rest.EndsWith("/parent", StringComparison.Ordinal) || System.Text.RegularExpressions.Regex.IsMatch(rest, "^service-orders/[^/]+/targets/(?!count$|extent$)[^/]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
                return RecordingKernelHandler.Problem(HttpStatusCode.NotFound, "targets.not-found");

            if (rest.StartsWith("order-types/", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (string.Equals(rest, "order-types", StringComparison.Ordinal)) return Json(HttpStatusCode.OK, new List<OrderType>());
        }

        if (request.Method == HttpMethod.Delete && rest.StartsWith("service-orders/", StringComparison.Ordinal) && !rest["service-orders/".Length..].Contains('/', StringComparison.Ordinal))
        {
            var deleted = Uri.UnescapeDataString(rest["service-orders/".Length..]);
            orders.RemoveAll(o => string.Equals(o.Id, deleted, StringComparison.Ordinal));
        }

        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }
}
