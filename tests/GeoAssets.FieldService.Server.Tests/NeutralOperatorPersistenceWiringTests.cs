using FluentAssertions;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// XD01-380 on the real <c>GeoAssets.FieldService.Server</c> <c>Program</c>: the host registers the NeutralOperator vertical's
/// <c>NeutralOperatorDbContext</c> and the occupancy ports on it, over its <c>ServiceOrderDbContext</c>'s connection. Nothing
/// here opens a database connection: resolving a <c>DbContext</c> only builds its options.
/// </summary>
public sealed class NeutralOperatorPersistenceWiringTests
{
    private const string FieldServiceConnectionString =
        "Host=localhost;Port=1;Database=fieldservice_wiring_test;Username=boot;Password=boot;Timeout=1";

    [Fact]
    public void Host_ResolvesTheVerticalsContextAndOccupancyPorts_OverTheKernelContextsConnection()
    {
        using var factory = new WebApplicationFactory<GeoCoreAssetQueryAdapter>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ConnectionStrings:FieldService", FieldServiceConnectionString);
        });
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        var neutralOperator = provider.GetRequiredService<NeutralOperatorDbContext>();
        var kernel = provider.GetRequiredService<ServiceOrderDbContext>();

        neutralOperator.Database.IsNpgsql().Should().BeTrue();
        neutralOperator.Database.GetDbConnection().Should().BeSameAs(kernel.Database.GetDbConnection());
        provider.GetRequiredService<IPoleOccupancyReader>().Should().BeOfType<EFPoleOccupancyRepository>()
            .And.BeSameAs(provider.GetRequiredService<IPoleOccupancyWriter>());
        provider.GetRequiredService<IOccupancyChangeSetReader>().Should().BeOfType<EFOccupancyChangeSetRepository>()
            .And.BeSameAs(provider.GetRequiredService<IOccupancyChangeSetWriter>())
            .And.BeSameAs(provider.GetRequiredService<IOccupancyChangeSetCloser>());
        provider.GetServices<IOrderLifecycleHook>().Should().ContainSingle(hook => hook is OccupancyReleaseLifecycleHook);
        kernel.Model.GetEntityTypes().Select(entity => entity.GetTableName())
            .Should().NotContain(["field_pole_occupancies", "field_pole_occupancy_entries", "ServiceOrderOccupancyChangeSets"]);
    }
}
