using System.Net;
using FluentAssertions;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// XD01-390 on the NeutralOperator host: it forwards the caller's token to the kernel's <c>/api/identity/me</c> for every
/// permission check, so a kernel that does not accept the token's audience (401) is a distinct, actionable 502
/// <c>kernel.token-rejected</c>, a kernel that is down is 503 <c>kernel.unavailable</c>, and the normal path is unchanged.
/// </summary>
public sealed class KernelTokenRejectionTests
{
    private const string Order = "no-audience-1";

    [Fact]
    public async Task WhenTheKernelRefusesTheForwardedToken_AnAuthorizedRoute_Answers502TokenRejected_WithTheKernelsDescription()
    {
        await using var host = await NeutralOperatorHost.StartAsync(kernel => kernel.IdentityRejection = "The audience 'neutral-operator-api' is invalid");
        using var owner = host.ClientFor("owner-admin");

        var problem = await (await owner.GetOrderAsync(Order)).ShouldBeProblemAsync(HttpStatusCode.BadGateway, "kernel.token-rejected");

        var detail = problem.GetProperty("detail").GetString();
        detail.Should().Contain("AzureAdCiam:AdditionalAudiences").And.Contain("The kernel said: The audience 'neutral-operator-api' is invalid");
        host.Kernel.To(HttpMethod.Get, "/api/identity/me").Should().NotBeEmpty();
    }

    [Fact]
    public async Task WhenTheKernelRefusesTheForwardedToken_TheCreateRoute_IsRefusedTheSameWay_AndNothingIsStored()
    {
        await using var host = await NeutralOperatorHost.StartAsync(kernel => kernel.IdentityRejection = "nope");
        using var owner = host.ClientFor("owner-admin");

        await (await owner.CreateOrderAsync(Order)).ShouldBeProblemAsync(HttpStatusCode.BadGateway, "kernel.token-rejected");

        host.Kernel.To(HttpMethod.Post, "/reserve").Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheKernelIsDown_AnAuthorizedRoute_Answers503KernelUnavailable_NotTokenRejected()
    {
        await using var host = await NeutralOperatorHost.StartAsync(kernel => kernel.IdentityFails = true);
        using var owner = host.ClientFor("owner-admin");

        await (await owner.GetOrderAsync(Order)).ShouldBeProblemAsync(HttpStatusCode.ServiceUnavailable, "kernel.unavailable");
    }

    [Fact]
    public async Task WhenTheKernelAcceptsTheToken_TheRoutesWorkAsBefore_AndTheCallersOwnTokenWasForwarded()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");

        var created = await owner.CreateOrderAsync(Order);
        var read = await owner.GetOrderAsync(Order);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Kernel.To(HttpMethod.Get, "/api/identity/me").Should().NotBeEmpty()
            .And.OnlyContain(r => r.Authorization == "Bearer owner-admin");
    }

    [Fact]
    public async Task ARequestWithoutAToken_IsStill401FromThisHost_AndNeverReachesTheKernelsIdentityRoute()
    {
        await using var host = await NeutralOperatorHost.StartAsync(kernel => kernel.IdentityRejection = "nope");
        using var anonymous = host.ClientFor(token: null);

        (await anonymous.GetOrderAsync(Order)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Kernel.To(HttpMethod.Get, "/api/identity/me").Should().BeEmpty();
    }
}
