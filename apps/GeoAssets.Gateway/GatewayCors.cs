namespace GeoAssets.Gateway;

/// <summary>
/// CORS at the gateway edge (XD01-384): the browser talks only to the gateway, so the gateway is the one place that
/// answers preflights and sets <c>Access-Control-*</c> headers — for every route, the vertical ones included — with the
/// same <c>Cors:AllowedOrigins</c> setting and the same policy shape (any header, any method, no credentials) as
/// <c>GeoAssets.Server</c> and <c>GeoAssets.NeutralOperator.Server</c>. Preflights never reach a backend (the CORS
/// middleware answers them before routing to the proxy), and the backends' own CORS response headers are dropped by
/// every route (<see cref="GatewayRouteCatalog.BackendCorsResponseHeaders"/>), so a response never carries two
/// <c>Access-Control-Allow-Origin</c> headers and the backends' CORS settings no longer matter behind the gateway.
/// </summary>
internal static class GatewayCors
{
    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    public static IServiceCollection AddGatewayCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var allowedOrigins = configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins([.. allowedOrigins.Where(origin => !string.IsNullOrWhiteSpace(origin))])
            .AllowAnyHeader()
            .AllowAnyMethod()));

        return services;
    }
}
