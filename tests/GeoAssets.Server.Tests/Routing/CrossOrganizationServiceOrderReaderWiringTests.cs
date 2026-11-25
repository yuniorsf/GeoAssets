using FluentAssertions;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// XD01-408 on the real <c>Program</c>: the kernel registers the grant-aware read path, so its <c>GET service-orders</c>
/// lists the orders its callers' grants reach — over the request's own <see cref="EFServiceOrderRepository"/>, whatever modules
/// are enabled.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class CrossOrganizationServiceOrderReaderWiringTests
{
    [Fact]
    public async Task Server_RegistersTheReadPath_AsTheScopesOwnRepository()
    {
        var factory = await ServerHostFixture.GetKernelOnlyFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<ICrossOrganizationServiceOrderReader>();

        reader.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<EFServiceOrderRepository>());
    }

    [Fact]
    public void AddCrossOrganizationServiceOrderReader_KeepsAReaderAlreadyRegistered()
    {
        var existing = new ServiceDescriptor(typeof(ICrossOrganizationServiceOrderReader), _ => null!, ServiceLifetime.Scoped);
        IServiceCollection services = new ServiceCollection();
        services.Add(existing);

        services.AddCrossOrganizationServiceOrderReader();

        services.Where(d => d.ServiceType == typeof(ICrossOrganizationServiceOrderReader)).Should().Equal(existing);
        ((Action)(() => WorkflowEFCoreServiceExtensions.AddCrossOrganizationServiceOrderReader(null!))).Should().Throw<ArgumentNullException>();
    }
}
