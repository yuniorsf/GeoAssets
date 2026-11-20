using System.Net;
using FluentAssertions;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// The host's liveness and readiness (XD01-382): while the kernel does not know this vertical yet, <c>/healthz</c> stays 200
/// (reporting <c>degraded</c>, so no orchestrator restarts the host for a kernel outage) and <c>/readyz</c> is 503; once the
/// registration succeeds both report healthy. Neither needs a token.
/// </summary>
public sealed class HostHealthTests
{
    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = Task.Delay(TimeSpan.FromSeconds(30));
        while (!await condition())
        {
            if (deadline.IsCompleted) throw new TimeoutException("The host did not reach the expected health in time.");
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task WhileTheKernelRefusesTheRegistration_HealthzIs200Degraded_AndReadyzIs503()
    {
        await using var host = await NeutralOperatorHost.StartAsync(kernel => kernel.RegistrationFails = true);
        using var anonymous = host.ClientFor(null);
        await UntilAsync(async () => (await GetAsync(anonymous, "/healthz")).Body.Contains("degraded", StringComparison.Ordinal)
            && host.Kernel.To(HttpMethod.Put, "/order-types").Count >= 2);

        var health = await GetAsync(anonymous, "/healthz");
        var ready = await GetAsync(anonymous, "/readyz");

        health.Status.Should().Be(HttpStatusCode.OK);
        health.Body.Should().Be("""{"status":"degraded"}""");
        ready.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
        host.Kernel.To(HttpMethod.Put, "/permissions").Should().BeEmpty("the permissions are published only after the order types were accepted");
    }

    [Fact]
    public async Task OnceTheKernelAcceptsTheRegistration_BothAreHealthy_WithoutARestart()
    {
        await using var host = await NeutralOperatorHost.StartAsync(kernel => kernel.RegistrationFails = true);
        using var anonymous = host.ClientFor(null);
        await UntilAsync(async () => (await GetAsync(anonymous, "/readyz")).Status == HttpStatusCode.ServiceUnavailable
            && host.Kernel.To(HttpMethod.Put, "/order-types").Count >= 1);

        host.Kernel.RegistrationFails = false;
        await UntilAsync(async () => (await GetAsync(anonymous, "/readyz")).Status == HttpStatusCode.OK);

        var health = await GetAsync(anonymous, "/healthz");
        health.Status.Should().Be(HttpStatusCode.OK);
        health.Body.Should().Be("""{"status":"healthy"}""");
        (await GetAsync(anonymous, "/readyz")).Body.Should().NotBeNull();
    }

    [Fact]
    public async Task TheRegistrationTheHostMakes_ReachesTheKernelAsTheServiceCaller_WithTheVerticalsDescriptorsAndPermissions()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var anonymous = host.ClientFor(null);
        await UntilAsync(async () => (await GetAsync(anonymous, "/readyz")).Status == HttpStatusCode.OK);

        var orderTypes = host.Kernel.To(HttpMethod.Put, "/order-types").Should().ContainSingle().Subject;
        var permissions = host.Kernel.To(HttpMethod.Put, "/permissions").Should().ContainSingle().Subject;

        orderTypes.Path.Should().Be("/api/registry/verticals/neutral-operator/order-types");
        permissions.Path.Should().Be("/api/identity/verticals/neutral-operator/permissions");
        orderTypes.Authorization.Should().Be($"Bearer {FakeServiceAuthTokenProvider.Token}");
        permissions.Authorization.Should().Be($"Bearer {FakeServiceAuthTokenProvider.Token}");
        orderTypes.Json.EnumerateArray().Select(d => d.GetProperty("orderTypeId").GetString()).Should().Contain("shared-use-pole-work");
        orderTypes.Json.EnumerateArray().Should().OnlyContain(d => d.GetProperty("terminalStatuses").GetArrayLength() > 0);
        permissions.Json.GetProperty("roleBundles").EnumerateArray().Single().GetProperty("roleName").GetString().Should().Be("IspTenant");
        host.Kernel.Requests.Select(r => r.Method.Method + " " + r.Path).Should().ContainInOrder(
            "PUT /api/registry/verticals/neutral-operator/order-types", "PUT /api/identity/verticals/neutral-operator/permissions");
    }
}
