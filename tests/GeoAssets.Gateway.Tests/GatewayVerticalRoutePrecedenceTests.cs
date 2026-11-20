using FluentAssertions;
using Xunit;

namespace GeoAssets.Gateway.Tests;

/// <summary>XD01-384: route precedence of the vertical's prefix and the kernel's catch-all, with a dedicated silo for the tenant.</summary>
public sealed class GatewayVerticalRoutePrecedenceTests : IAsyncLifetime
{
    private const string Vertical = "/api/v/neutral-operator/workflow/service-orders";
    private const string Kernel = "/api/workflow/service-orders";

    private RecordingBackend _kernel = null!;
    private RecordingBackend _kernelSilo = null!;
    private RecordingBackend _vertical = null!;
    private RecordingBackend _verticalSilo = null!;

    public async Task InitializeAsync()
    {
        _kernel = await RecordingBackend.StartAsync("kernel");
        _kernelSilo = await RecordingBackend.StartAsync("kernel-silo");
        _vertical = await RecordingBackend.StartAsync("vertical");
        _verticalSilo = await RecordingBackend.StartAsync("vertical-silo");
    }

    public async Task DisposeAsync()
    {
        await _kernel.DisposeAsync();
        await _kernelSilo.DisposeAsync();
        await _vertical.DisposeAsync();
        await _verticalSilo.DisposeAsync();
    }

    private GatewayFactory Gateway() => GatewayFactory.For(
        _kernel,
        _vertical,
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Destinations:GeoAssets:Silos:acme.example.com"] = _kernelSilo.Address,
            ["Destinations:NeutralOperator:Silos:acme.example.com"] = _verticalSilo.Address,
        });

    private async Task<IReadOnlyList<string>> HitsAsync(string path, string? tenantDomain)
    {
        await using var factory = Gateway();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (tenantDomain is not null) request.Headers.Add(GatewayRouteCatalog.TenantDomainHeader, tenantDomain);

        await client.SendAsync(request);

        return new[] { ("kernel", _kernel), ("kernel-silo", _kernelSilo), ("vertical", _vertical), ("vertical-silo", _verticalSilo) }
            .Where(b => b.Item2.Requests.Count > 0).Select(b => b.Item1).ToList();
    }

    [Fact]
    public async Task AVerticalPath_WithoutATenantDomain_ReachesTheVerticalsSharedHost_Only() =>
        (await HitsAsync(Vertical, null)).Should().Equal("vertical");

    [Fact]
    public async Task AVerticalPath_OfASiloTenant_ReachesTheVerticalsSilo_NotTheKernelsCatchAllOrSilo() =>
        (await HitsAsync(Vertical, "acme.example.com")).Should().Equal("vertical-silo");

    [Fact]
    public async Task AVerticalPath_OfATenantWithoutASilo_FallsThroughToTheVerticalsSharedHost() =>
        (await HitsAsync(Vertical, "other.example.com")).Should().Equal("vertical");

    [Fact]
    public async Task AKernelPath_WithoutATenantDomain_ReachesTheKernelsSharedHost() =>
        (await HitsAsync(Kernel, null)).Should().Equal("kernel");

    [Fact]
    public async Task AKernelPath_OfASiloTenant_ReachesTheKernelsSilo_NotTheVertical() =>
        (await HitsAsync(Kernel, "acme.example.com")).Should().Equal("kernel-silo");

    [Fact]
    public async Task AnOgcPath_OfASiloTenant_ReachesTheKernelsSilo() =>
        (await HitsAsync("/wms", "acme.example.com")).Should().Equal("kernel-silo");

    [Fact]
    public async Task TheVerticalsPrefix_IsNotMatchedByALookalike_WhichFallsToTheKernelCatchAll() =>
        (await HitsAsync("/api/v/neutral-operators/x", null)).Should().Equal("kernel");

    [Fact]
    public async Task AnotherVerticalsPrefix_IsNotRoutedToTheNeutralOperatorHost() =>
        (await HitsAsync("/api/v/other-vertical/workflow/service-orders", null)).Should().Equal("kernel");
}
