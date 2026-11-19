using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// XD01-374: <see cref="PostgresMutationBatchServiceExtensions.AddPostgresAssetMutationBatchApplier"/> owns the
/// <see cref="IAssetMutationBatchApplier"/> registration — refreshing the organization catalog after each batch when the host
/// serves <see cref="PostgresOrganizationAssetCatalogs"/>, the raw applier otherwise. Nothing here opens a connection.
/// </summary>
public sealed class PostgresMutationBatchServiceExtensionsTests
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    [Fact]
    public async Task AddPostgresAssetMutationBatchApplier_WithOrganizationCatalogs_RegistersTheRefreshingApplier()
    {
        var services = Services();
        var dbOptions = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        // Lazy: the provider opens no connection until its first read.
        services.AddSingleton<IAssetProvider>(new PostgresAssetProvider(
            new GeoAssetsDbContext(dbOptions), dbOptions, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System));
        services.AddPostgresOrganizationAssetCatalogs(ConnectionString);
        services.AddPostgresAssetMutationBatchApplier(ConnectionString);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAssetMutationBatchApplier>().Should().BeOfType<OrganizationCatalogRefreshingApplier>();
        provider.GetRequiredService<PostgresAssetMutationBatchApplier>().Should().NotBeNull();
    }

    [Fact]
    public void AddPostgresAssetMutationBatchApplier_WithoutOrganizationCatalogs_RegistersTheRawApplier()
    {
        var services = Services();
        services.AddPostgresAssetMutationBatchApplier(ConnectionString);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAssetMutationBatchApplier>()
            .Should().BeSameAs(provider.GetRequiredService<PostgresAssetMutationBatchApplier>());
    }

    [Fact]
    public void AddPostgresAssetMutationBatchApplier_AddsATimeProvider_WhenNoneIsRegistered()
    {
        var services = Services();
        services.AddPostgresAssetMutationBatchApplier(ConnectionString);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddPostgresAssetMutationBatchApplier_InvalidArguments_Throw()
    {
        ((Action)(() => PostgresMutationBatchServiceExtensions.AddPostgresAssetMutationBatchApplier(null!, ConnectionString)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new ServiceCollection().AddPostgresAssetMutationBatchApplier(" ")))
            .Should().Throw<ArgumentException>();
    }
}
