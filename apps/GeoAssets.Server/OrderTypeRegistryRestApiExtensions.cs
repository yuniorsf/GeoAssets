using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Registry;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// The kernel's order-type registry (XD01-369, ADR-001 Part 1.1): verticals publish their order-type descriptors as data,
/// and the kernel offers "actions according to the feature type's semantics" without running vertical code.
/// <list type="bullet">
///   <item><c>PUT /api/registry/verticals/{verticalId}/order-types</c> — idempotent replace of the vertical's whole set
///   (<see cref="RegistryManagePermission"/>; a <c>GeoAssets.ServiceCaller</c> service principal satisfies it).</item>
///   <item><c>GET /api/registry/order-types</c> — every published descriptor.</item>
///   <item><c>GET /api/geoassets/features/{id}/actions</c> — the feature actions matching the feature's asset-type roles
///   that the caller may act on (it may create an order of the action's order type).</item>
/// </list>
/// </summary>
public static class OrderTypeRegistryRestApiExtensions
{
    /// <summary>The permission that publishes a vertical's descriptors.</summary>
    public const string RegistryManagePermission = "registry:manage";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOrderTypeRegistryApi(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPut("/api/registry/verticals/{verticalId}/order-types", PublishAsync)
            .RequireAuthorization(RegistryManagePermission);

        routes.MapGet("/api/registry/order-types", (IOrderTypeDescriptorRegistry registry) => Results.Json(registry.GetAll(), Json))
            .RequireAuthorization("serviceorders:read");

        routes.MapGet("/api/geoassets/features/{id}/actions", FeatureActionsAsync)
            .RequireAuthorization("features:read");

        return routes;
    }

    private static async Task<IResult> PublishAsync(string verticalId, HttpRequest req, IOrderTypeDescriptorRegistry registry)
    {
        OrderTypeDescriptor[]? descriptors;
        try
        {
            descriptors = await JsonSerializer.DeserializeAsync<OrderTypeDescriptor[]>(req.Body, Json, req.HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new { error = $"Invalid descriptor set: {ex.Message}" });
        }

        if (OrderTypeDescriptorValidation.FindProblem(verticalId, descriptors) is { } problem)
            return Results.BadRequest(new { error = problem });

        var changed = registry.Upsert(verticalId, descriptors!);
        return Results.Ok(new { verticalId, changed });
    }

    private static async Task<IResult> FeatureActionsAsync(
        string id, IAssetProvider assets, IOrderTypeDescriptorRegistry registry, IAuthorizationService authorization, HttpContext http)
    {
        if (assets.GetById(id) is not { } feature) return Results.NotFound();

        var assetType = assets.GetAssetTypes()
            .FirstOrDefault(t => string.Equals(t.Id.ToString(), feature.Properties.AssetTypeId, StringComparison.OrdinalIgnoreCase));
        IReadOnlyCollection<string> roles = assetType?.Roles ?? [];

        var offered = new List<FeatureActionDescriptor>();
        foreach (var action in registry.FeatureActionsFor(roles))
        {
            var allowed = await authorization
                .AuthorizeAsync(http.User, resource: null, new ServiceOrderCreateRequirement(action.OrderTypeId))
                .ConfigureAwait(false);
            if (allowed.Succeeded) offered.Add(action);
        }

        return Results.Json(offered, Json);
    }
}
