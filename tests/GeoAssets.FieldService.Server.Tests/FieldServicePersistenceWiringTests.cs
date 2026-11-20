using FluentAssertions;
using GeoAssets.FieldService.Contractors;
using GeoAssets.FieldService.FieldWork;
using GeoAssets.FieldService.Infrastructure;
using GeoAssets.FieldService.Persistence;
using GeoAssets.Workflow.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// XD01-381 on the real <c>GeoAssets.FieldService.Server</c> <c>Program</c>: the host registers the FieldService vertical's
/// <c>FieldServiceDbContext</c>, the five stores on it and the inspection commit, over its <c>ServiceOrderDbContext</c>'s
/// connection. Nothing here opens a database connection: resolving a <c>DbContext</c> only builds its options.
/// </summary>
public sealed class FieldServicePersistenceWiringTests
{
    private const string FieldServiceConnectionString =
        "Host=localhost;Port=1;Database=fieldservice_wiring_test;Username=boot;Password=boot;Timeout=1";

    [Fact]
    public void Host_ResolvesTheVerticalsContextTheFiveStoresAndTheInspectionCommit_OverTheKernelContextsConnection()
    {
        using var factory = new WebApplicationFactory<GeoCoreAssetQueryAdapter>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ConnectionStrings:FieldService", FieldServiceConnectionString);
        });
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        var fieldService = provider.GetRequiredService<FieldServiceDbContext>();
        var kernel = provider.GetRequiredService<ServiceOrderDbContext>();

        fieldService.Database.IsNpgsql().Should().BeTrue();
        fieldService.Database.GetDbConnection().Should().BeSameAs(kernel.Database.GetDbConnection());
        fieldService.Database.GetMigrations().Should().ContainSingle().Which.Should().EndWith("_AdoptFieldServiceTables");
        provider.GetRequiredService<IContractorUsLedgerRepository>().Should().BeOfType<EFContractorUsLedgerRepository>();
        provider.GetRequiredService<IUsCatalogRepository>().Should().BeOfType<EFUsCatalogRepository>();
        provider.GetRequiredService<IContractorUsPostingRepository>().Should().BeOfType<EFContractorUsPostingRepository>();
        provider.GetRequiredService<IPavingMeasurementRepository>().Should().BeOfType<EFPavingMeasurementRepository>();
        provider.GetRequiredService<IManholeAuditRepository>().Should().BeOfType<EFManholeAuditRepository>();
        provider.GetRequiredService<InspectionCommitService>().Should().NotBeNull();
        provider.GetRequiredService<IAmbientDbTransaction>().Should().BeOfType<ServiceOrderAmbientTransaction>();
        kernel.Model.GetEntityTypes().Select(entity => entity.GetTableName())
            .Should().NotContain(["ContractorUsLedgerEntries", "UsCatalogItems", "field_contractor_us_postings", "field_icerp_paving_measurements", "field_manhole_audits"]);
    }
}
