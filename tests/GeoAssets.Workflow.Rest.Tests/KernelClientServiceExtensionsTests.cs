using System.Net;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// <see cref="KernelClientServiceExtensions.AddKernelServiceCallerClients"/> (XD01-382): both machine-to-machine clients of the kernel are
/// registered against its root URL — and a URL that is not an absolute HTTP(S) one is refused at startup, not at the first call.
/// </summary>
public sealed class KernelClientServiceExtensionsTests
{
    private static ServiceProvider Provider(string url, RecordingKernelHandler handler, bool withTokens = false)
    {
        var services = new ServiceCollection();
        if (withTokens) services.AddSingleton<IServiceAuthTokenProvider, FakeServiceAuthTokenProvider>();
        services.AddKernelServiceCallerClients(url);
        services.AddHttpClient(KernelClientServiceExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static RecordingKernelHandler NoContent() => new(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddKernelServiceCallerClients_WithABlankUrl_Throws(string url)
    {
        var act = () => new ServiceCollection().AddKernelServiceCallerClients(url);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddKernelServiceCallerClients_WithoutAUrl_Throws()
    {
        var act = () => new ServiceCollection().AddKernelServiceCallerClients(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("kernel")]
    [InlineData("/api/geoassets")]
    [InlineData("geoassets-server:8080")]
    [InlineData("ftp://kernel.test")]
    [InlineData("file:///kernel")]
    [InlineData("not a url")]
    [InlineData("http://")]
    public void AddKernelServiceCallerClients_WithAnUrlThatIsNotAbsoluteHttpOrHttps_Throws(string url)
    {
        var act = () => new ServiceCollection().AddKernelServiceCallerClients(url);

        act.Should().Throw<ArgumentException>().WithMessage($"*'{url}'*absolute HTTP(S) URL*");
    }

    [Fact]
    public void AddKernelServiceCallerClients_WithoutServices_Throws()
    {
        var act = () => KernelClientServiceExtensions.AddKernelServiceCallerClients(null!, "http://kernel.test");

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("http://geoassets-server:8080")]
    [InlineData("http://geoassets-server:8080/")]
    [InlineData("https://kernel.example.org")]
    [InlineData("HTTP://KERNEL.TEST")]
    public void AddKernelServiceCallerClients_WithAnAbsoluteHttpUrl_RegistersBothClients(string url)
    {
        using var provider = Provider(url, NoContent());

        provider.GetRequiredService<ITargetLedgerClient>().Should().BeOfType<RestTargetLedgerClient>();
        provider.GetRequiredService<IVerticalRegistrationClient>().Should().BeOfType<RestVerticalRegistrationClient>();
    }

    [Theory]
    [InlineData("http://geoassets-server:8080", "http://geoassets-server:8080/api/registry/verticals/neutral-operator/order-types")]
    [InlineData("http://geoassets-server:8080/", "http://geoassets-server:8080/api/registry/verticals/neutral-operator/order-types")]
    [InlineData("https://gateway.test/kernel", "https://gateway.test/kernel/api/registry/verticals/neutral-operator/order-types")]
    public async Task TheClients_AreRelativeToTheKernelsRoot_WhetherOrNotTheUrlEndsWithASlash(string url, string expected)
    {
        var handler = NoContent();
        using var provider = Provider(url, handler);

        await provider.GetRequiredService<IVerticalRegistrationClient>().PublishOrderTypesAsync("neutral-operator", [], CancellationToken.None);

        handler.Requests.Single().Original.Should().Be(expected);
    }

    [Fact]
    public async Task TheLedgerClient_UsesTheSameRoot_AndTheRegisteredServiceTokenProvider()
    {
        var handler = new RecordingKernelHandler(_ => RecordingKernelHandler.Json(
            HttpStatusCode.OK, """{ "verticalId": "neutral-operator", "orderId": "ord-1", "changed": 0, "targets": [] }"""));
        using var provider = Provider("http://geoassets-server:8080", handler, withTokens: true);

        await provider.GetRequiredService<ITargetLedgerClient>()
            .ReleaseAsync(new TargetLedgerKey("neutral-operator", "ord-1"), Guid.NewGuid(), null, null, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Original.Should().Be("http://geoassets-server:8080/api/targets/ledger/neutral-operator/ord-1/release");
        request.Authorization.Should().Be("Bearer service-token-1");
    }

    [Fact]
    public async Task WithoutARegisteredTokenProvider_TheClientsSendNoAuthorization()
    {
        var handler = NoContent();
        using var provider = Provider("http://geoassets-server:8080", handler);

        await provider.GetRequiredService<IVerticalRegistrationClient>().PublishOrderTypesAsync("neutral-operator", [], CancellationToken.None);

        handler.Requests.Single().Authorization.Should().BeNull();
    }

    [Fact]
    public void TheClients_AreTransient_SoEachResolutionGetsAnHttpClientFromTheFactory()
    {
        using var provider = Provider("http://geoassets-server:8080", NoContent());

        provider.GetRequiredService<ITargetLedgerClient>().Should().NotBeSameAs(provider.GetRequiredService<ITargetLedgerClient>());
        provider.GetRequiredService<IVerticalRegistrationClient>().Should().NotBeSameAs(provider.GetRequiredService<IVerticalRegistrationClient>());
    }

    [Fact]
    public void TheNamedClient_IsTheOneBothClientsShare()
    {
        KernelClientServiceExtensions.HttpClientName.Should().Be("GeoAssetsKernel:service-caller");
    }
}
