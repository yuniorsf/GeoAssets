using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// XD01-365: the real <c>Program</c> host, every module enabled, no longer maps the bespoke <c>/api/isp/tenant-portal</c>
/// routes — the tenant portal uses the generic service-order routes. Read from the host's endpoint table: the host answers
/// every unauthenticated request 401, mapped or not, so a request cannot tell a missing route from a protected one.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class TenantPortalRoutesGoneTests
{
    [Fact]
    public async Task PortalRoutes_AreGone()
    {
        var services = await ServerHostFixture.GetNeutralOperatorEnabledServicesAsync();

        var patterns = services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty);

        patterns.Should().NotContain(
            p => p.Contains("tenant-portal", StringComparison.OrdinalIgnoreCase), "the portal has no routes of its own");
    }
}
