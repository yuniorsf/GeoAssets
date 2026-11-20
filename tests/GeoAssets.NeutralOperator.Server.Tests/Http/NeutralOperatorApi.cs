using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>Requests and assertions on the chassis' <c>/api/workflow</c> routes, as this host serves them.</summary>
internal static class NeutralOperatorApi
{
    public const string PoleWork = "shared-use-pole-work";
    public const string Strategy = "identifier-list";

    public static Uri Route(string relative) => new($"/api/workflow/{relative}", UriKind.Relative);

    public static Task<HttpResponseMessage> CreateOrderAsync(this HttpClient client, string id, string orderTypeId = PoleWork) =>
        client.PostAsJsonAsync(Route("service-orders"), new { id, title = $"Order {id}", orderTypeId });

    public static Task<HttpResponseMessage> PreviewAsync(this HttpClient client, string orderId, params string[] featureIds) =>
        client.PostAsJsonAsync(Route($"service-orders/{orderId}/targets/selection/preview"), Selection(featureIds));

    public static Task<HttpResponseMessage> ApplyAsync(this HttpClient client, string orderId, params string[] featureIds) =>
        client.PostAsJsonAsync(Route($"service-orders/{orderId}/targets/selection/apply"), Selection(featureIds));

    public static Task<HttpResponseMessage> GetOrderAsync(this HttpClient client, string orderId) =>
        client.GetAsync(Route($"service-orders/{orderId}"));

    public static Task<HttpResponseMessage> GetTargetsAsync(this HttpClient client, string orderId, int pageSize = 200) =>
        client.GetAsync(Route($"service-orders/{orderId}/targets?pageSize={pageSize}"));

    public static Task<HttpResponseMessage> CancelAsync(this HttpClient client, string orderId)
    {
        OrderAction cancel = OrderActionType.Cancel;
        return client.PostAsJsonAsync(
            Route($"service-orders/{orderId}/actions"),
            new { action = cancel, performedBy = "ignored", performedAt = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc), resultingStatus = ServiceOrderStatus.Cancelled });
    }

    public static async Task<string> StatusOfAsync(this HttpClient client, string orderId) =>
        (await (await client.GetOrderAsync(orderId)).ReadJsonAsync()).GetProperty("status").GetString()!;

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, "the body was: {0}", text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Asserts an <c>application/problem+json</c> answer with the given status and stable <c>code</c>; returns its body.</summary>
    public static async Task<JsonElement> ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, "the body was: {0}", text);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = JsonDocument.Parse(text).RootElement.Clone();
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("status").GetInt32().Should().Be((int)status);
        return body;
    }

    public static string[] Strings(this JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private static object Selection(string[] featureIds) => new
    {
        strategyId = Strategy,
        parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["featureIds"] = featureIds },
    };
}
