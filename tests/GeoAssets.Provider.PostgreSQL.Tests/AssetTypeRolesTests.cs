using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// The <c>AddAssetTypeRoles</c> migration and the <c>asset_type."Roles"</c> column behind
/// <see cref="AssetType.Roles"/> (XD01-367), against the real PostGIS instance the collection
/// fixture migrates. Every test that inserts a roles-bearing custom type deletes it again: other
/// tests of this collection migrate down past this migration, and its Down guard refuses while
/// such a type exists.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AssetTypeRolesTests(PostgresContainerFixture fixture)
{
    private const string PreviousMigration = "20261117012216_SeedPoleOccupancyThematicLayers";
    private static readonly Guid PoleId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    private static PostgresAssetProvider CreateSut(DbContextOptions<GeoAssetsDbContext> options) =>
        new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);

    public static TheoryData<string[]> RoleSets => new()
    {
        Array.Empty<string>(),
        new[] { "occupiable-support" },
        new[] { "isp:pole" },
        new[] { "occupiable-support", "isp:pole", "x-y:z-1" },
    };

    [Theory]
    [MemberData(nameof(RoleSets))]
    public async Task AddAssetType_ThenReadFromAFreshProvider_RoundTripsTheRoles(string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var assetType = new AssetType { Name = $"Roles {roles.Length}", Roles = roles };
        try
        {
            await using (var writer = CreateSut(fixture.CreateOptions()))
                writer.AddAssetType(assetType);

            await using var reader = CreateSut(fixture.CreateOptions());
            var reloaded = reader.GetAssetTypes().Single(t => t.Id == assetType.Id);

            reloaded.Roles.Should().Equal(roles);
        }
        finally
        {
            await using var cleanup = CreateSut(fixture.CreateOptions());
            cleanup.DeleteAssetType(assetType.Id);
        }
    }

    [Fact]
    public async Task SeededPoleAssetType_ExposesOccupiableSupport()
    {
        await using var sut = CreateSut(fixture.CreateOptions());

        var pole = sut.GetAssetTypes().Single(t => t.Id == PoleId);

        pole.Roles.Should().Contain("occupiable-support").And.Contain("isp:pole");
        ((IAssetProvider)sut).GetAssetTypesWithRole("occupiable-support").Select(t => t.Id).Should().Contain(PoleId);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000009", "ip-billing:transformer")]
    [InlineData("00000000-0000-0000-0000-00000000000a", "ip-billing:pole")]
    [InlineData("00000000-0000-0000-0000-00000000000b", "ip-billing:lamp-group-24h")]
    public async Task SeededIpBillingAssetTypes_CarryTheirRoles(string id, string role)
    {
        await using var sut = CreateSut(fixture.CreateOptions());

        var seeded = sut.GetAssetTypes().Single(t => t.Id == Guid.Parse(id));

        seeded.Roles.Should().Equal(role);
    }

    [Fact]
    public async Task SeededAssetTypes_WithoutRoles_ReadAsEmpty()
    {
        await using var sut = CreateSut(fixture.CreateOptions());
        var withRoles = new[] { "0004", "0009", "000a", "000b" }.Select(s => Guid.Parse($"00000000-0000-0000-0000-00000000{s}"));

        var others = sut.GetAssetTypes().Where(t => t.IsBuiltIn && !withRoles.Contains(t.Id)).ToList();

        others.Should().NotBeEmpty();
        others.Should().OnlyContain(t => t.Roles.Count == 0);
    }

    [Fact]
    public async Task RolesColumn_HasGinIndex()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());

        (await IndexDefinitionsAsync(db)).Should().ContainSingle().Which.Should().Contain("USING gin").And.Contain("\"Roles\"");
    }

    [Fact]
    public async Task Migration_DownWhileACustomTypeCarriesRoles_IsRefusedAndKeepsTheColumn()
    {
        var assetType = new AssetType { Name = "Guarded", Roles = ["custom-role"] };
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await using (var writer = CreateSut(fixture.CreateOptions()))
                writer.AddAssetType(assetType);

            var act = () => migrator.MigrateAsync(PreviousMigration);

            var thrown = await act.Should().ThrowAsync<Exception>();
            thrown.Which.ToString().Should().Contain("AddAssetTypeRoles cannot be reverted");
            (await RolesColumnCountAsync(db)).Should().Be(1);
        }
        finally
        {
            await migrator.MigrateAsync();
            await using var cleanup = CreateSut(fixture.CreateOptions());
            cleanup.DeleteAssetType(assetType.Id);
        }
    }

    [Fact]
    public async Task Migration_DownOnceTheCustomRolesAreGone_DropsTheColumnAndUpRestoresIt()
    {
        var assetType = new AssetType { Name = "Guarded then removed", Roles = ["custom-role"] };
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await using (var writer = CreateSut(fixture.CreateOptions()))
            {
                writer.AddAssetType(assetType);
                writer.DeleteAssetType(assetType.Id);
            }

            await migrator.MigrateAsync(PreviousMigration);

            (await RolesColumnCountAsync(db)).Should().Be(0);
            (await IndexDefinitionsAsync(db)).Should().BeEmpty();
        }
        finally
        {
            await migrator.MigrateAsync();
            await using var cleanup = CreateSut(fixture.CreateOptions());
            cleanup.DeleteAssetType(assetType.Id);
        }

        (await RolesColumnCountAsync(db)).Should().Be(1);
        await using var reader = CreateSut(fixture.CreateOptions());
        reader.GetAssetTypes().Single(t => t.Id == PoleId).Roles.Should().Contain("occupiable-support");
    }

    private static async Task<int> RolesColumnCountAsync(GeoAssetsDbContext db)
    {
        await using var command = await CreateCommandAsync(db);
        command.CommandText = """
            SELECT column_name FROM information_schema.columns
            WHERE table_name = 'asset_type' AND column_name = 'Roles'
            """;
        return (await ReadAllAsync(command)).Count;
    }

    private static async Task<List<string>> IndexDefinitionsAsync(GeoAssetsDbContext db)
    {
        await using var command = await CreateCommandAsync(db);
        command.CommandText = """
            SELECT indexdef FROM pg_indexes
            WHERE tablename = 'asset_type' AND indexname = 'IX_asset_type_Roles'
            """;
        return await ReadAllAsync(command);
    }

    private static async Task<System.Data.Common.DbCommand> CreateCommandAsync(GeoAssetsDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        return connection.CreateCommand();
    }

    private static async Task<List<string>> ReadAllAsync(System.Data.Common.DbCommand command)
    {
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }
}
