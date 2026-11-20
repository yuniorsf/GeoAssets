using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>
/// XD01-385: the IpBilling host's bootstrap registration with the kernel — its permissions only, as the service principal, no
/// order-type descriptors — and the probes: <c>/healthz</c> stays 200 (Degraded) while the kernel does not know the host,
/// <c>/readyz</c> answers 503 until it does.
/// </summary>
public sealed class IpBillingRegistrationEndToEndTests
{
    private static readonly string[] IpBillingPermissionCodes =
    [
        "ip-billing:read", "ip-billing:upload", "ip-billing:calculate", "ip-billing:validate",
        "ip-billing:ledger-manual", "ip-billing:ledger-recarga", "ip-billing:reference-manage",
    ];

    private static async Task<HttpStatusCode> StatusAsync(IpBillingBridgeHost host, string path)
    {
        using var client = host.ClientFor(token: null);
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return response.StatusCode;
    }

    private static async Task<(HttpStatusCode Status, string? State)> HealthAsync(IpBillingBridgeHost host)
    {
        using var client = host.ClientFor(token: null);
        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, document.RootElement.GetProperty("status").GetString());
    }

    private static async Task<List<string>> PublishedCodesAsync(IpBillingBridgeHost host)
    {
        await using var db = host.Kernel.NewReadContext();
        return await db.Permissions.Where(p => p.Resource == "ip-billing").Select(p => p.Code).ToListAsync();
    }

    [Fact]
    public async Task Startup_PublishesTheIpBillingPermissions_AsTheServiceCaller_AndNoOrderTypes()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        await IpBillingBridgeHost.UntilAsync(() => host.Kernel.To(HttpMethod.Put, "/api/identity/verticals/ip-billing/permissions").Count > 0);
        await IpBillingBridgeHost.UntilAsync(async () => await StatusAsync(host, "/readyz") == HttpStatusCode.OK);

        var put = host.Kernel.To(HttpMethod.Put, "/api/identity/verticals/")[0];
        put.Path.Should().Be("/api/identity/verticals/ip-billing/permissions");
        put.Token.Should().Be(BridgeCallers.ServiceCallerToken);
        (await PublishedCodesAsync(host)).Should().BeEquivalentTo(IpBillingPermissionCodes);
        host.Kernel.Requests.Should().NotContain(r => r.Path.Contains("/api/registry/", StringComparison.Ordinal), "this vertical publishes no order types");
        host.Kernel.Requests.Where(r => r.Method == HttpMethod.Put).Should().OnlyContain(r => r.Path == "/api/identity/verticals/ip-billing/permissions");
    }

    [Fact]
    public async Task TheRegistration_IsRepublishedOnTheRefreshInterval_StillWithoutOrderTypes()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        await IpBillingBridgeHost.UntilAsync(() => host.Kernel.To(HttpMethod.Put, "/api/identity/verticals/ip-billing/permissions").Count >= 3);

        host.Kernel.Requests.Should().NotContain(r => r.Path.Contains("/api/registry/", StringComparison.Ordinal));
        (await PublishedCodesAsync(host)).Should().HaveCount(7, "republishing is an idempotent upsert");
    }

    [Fact]
    public async Task WhileTheKernelRefusesTheRegistration_HealthzIs200Degraded_AndReadyzIs503_ThenBothTurnHealthy()
    {
        await using var host = await IpBillingBridgeHost.StartAsync(kernelRegistrationFails: true);
        await IpBillingBridgeHost.UntilAsync(() => host.Kernel.To(HttpMethod.Put, "/api/identity/verticals/ip-billing/permissions").Count >= 2);

        var notReadyHealth = await HealthAsync(host);
        var notReady = await StatusAsync(host, "/readyz");

        host.Kernel.RegistrationFails = false;
        await IpBillingBridgeHost.UntilAsync(async () => await StatusAsync(host, "/readyz") == HttpStatusCode.OK);
        var readyHealth = await HealthAsync(host);

        notReadyHealth.Should().Be((HttpStatusCode.OK, "degraded"), "a kernel that is not ready must never restart this host");
        notReady.Should().Be(HttpStatusCode.ServiceUnavailable);
        readyHealth.Should().Be((HttpStatusCode.OK, "healthy"));
    }

    [Fact]
    public async Task WhenTheKernelIsUnreachable_TheHostIsNotReady_UntilItCanRegister()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();
        await IpBillingBridgeHost.UntilAsync(async () => await StatusAsync(host, "/readyz") == HttpStatusCode.OK);

        host.Link.Down = true;
        await IpBillingBridgeHost.UntilAsync(async () => await StatusAsync(host, "/readyz") == HttpStatusCode.ServiceUnavailable);
        var health = await HealthAsync(host);
        host.Link.Down = false;
        await IpBillingBridgeHost.UntilAsync(async () => await StatusAsync(host, "/readyz") == HttpStatusCode.OK);

        health.Should().Be((HttpStatusCode.OK, "degraded"));
    }

    [Fact]
    public async Task TheProbes_NeedNoBearerToken()
    {
        await using var host = await IpBillingBridgeHost.StartAsync();

        (await StatusAsync(host, "/healthz")).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(host, "/readyz")).Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task TheHost_RegistersNoOrderTypeDescriptorsOfItsOwn_ItsOrderTypesStayItsOwn()
    {
        // The IpBilling host keeps its own order types (AddOrderTypeRegistry) but publishes none to the kernel (permissions only).
        await using var host = await IpBillingBridgeHost.StartAsync();
        await IpBillingBridgeHost.UntilAsync(() => host.Kernel.To(HttpMethod.Put, "/api/identity/verticals/ip-billing/permissions").Count > 0);

        host.Kernel.To(HttpMethod.Put, "/order-types").Should().BeEmpty();
    }
}
