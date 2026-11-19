using System.Reflection;
using FluentAssertions;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// XD01-374: the design-time factory lives in the provider assembly, so <c>dotnet ef</c> runs with the provider project as its own
/// startup project. It reads <c>ConnectionStrings:GeoAssets</c> (environment <c>ConnectionStrings__GeoAssets</c>), else a local
/// default. Environment-mutating, so kept in its own collection; no connection is opened.
/// </summary>
[Collection("ProcessEnvironment")]
public sealed class GeoAssetsDbContextFactoryTests
{
    private const string EnvironmentVariable = "ConnectionStrings__GeoAssets";
    private const string LocalDefault = "Host=localhost;Port=5432;Database=geoassets;Username=postgres";

    private static Type FactoryType() =>
        typeof(PostgresProviderFactory).Assembly.GetType("GeoAssets.Provider.PostgreSQL.Data.GeoAssetsDbContextFactory", throwOnError: true)!;

    private static string ConnectionStringFor(string? environmentValue)
    {
        var previous = Environment.GetEnvironmentVariable(EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(EnvironmentVariable, environmentValue);

            var factory = (IDesignTimeDbContextFactory<GeoAssetsDbContext>)Activator.CreateInstance(FactoryType(), nonPublic: true)!;
            using var context = factory.CreateDbContext([]);
            return context.Database.GetConnectionString()!;
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void GeoAssetsDbContextFactory_LivesInTheProviderAssembly_AndIsADesignTimeFactoryForTheContext()
    {
        var type = FactoryType();

        type.Assembly.Should().BeSameAs(typeof(GeoAssetsDbContext).Assembly);
        type.Should().Implement<IDesignTimeDbContextFactory<GeoAssetsDbContext>>();
    }

    [Fact]
    public void CreateDbContext_WithoutAppSettingsOrEnvironmentVariable_UsesTheLocalDefault()
    {
        var connectionString = ConnectionStringFor(environmentValue: null);

        new NpgsqlConnectionStringBuilder(connectionString).ConnectionString
            .Should().Be(new NpgsqlConnectionStringBuilder(LocalDefault).ConnectionString);
    }

    [Fact]
    public void CreateDbContext_WithTheEnvironmentVariable_UsesIt()
    {
        const string fromEnvironment = "Host=db.example;Port=5433;Database=other;Username=svc";

        ConnectionStringFor(fromEnvironment).Should().Be(fromEnvironment);
    }

    [Fact]
    public void CreateDbContext_ConfiguresNpgsql()
    {
        var factory = (IDesignTimeDbContextFactory<GeoAssetsDbContext>)Activator.CreateInstance(FactoryType(), nonPublic: true)!;

        using var context = factory.CreateDbContext([]);

        context.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
    }
}
