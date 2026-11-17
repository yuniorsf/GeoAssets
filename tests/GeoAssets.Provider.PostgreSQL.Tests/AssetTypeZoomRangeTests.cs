using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Migrations;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// The <c>AddAssetTypeZoomRange</c> migration and the <c>asset_type."MinZoom"/"MaxZoom"</c>
/// columns behind <see cref="AssetType.MinZoom"/>/<see cref="AssetType.MaxZoom"/> (XD01-311),
/// against the real PostGIS instance the collection fixture migrates.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AssetTypeZoomRangeTests(PostgresContainerFixture fixture)
{
    private const string ZoomMigration = "20261116230000_AddAssetTypeZoomRange";
    private const string PreviousMigration = "20261116210000_GrantGeoEntityAttributeToAppRole";

    private sealed class ExposedMigration : AddAssetTypeZoomRange
    {
        public new void Up(MigrationBuilder migrationBuilder) => base.Up(migrationBuilder);
        public new void Down(MigrationBuilder migrationBuilder) => base.Down(migrationBuilder);
    }

    private static PostgresAssetProvider CreateSut(DbContextOptions<GeoAssetsDbContext> options) =>
        new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);

    [Theory]
    [InlineData(13, 15)]
    [InlineData(0, 24)]
    [InlineData(15, null)]
    [InlineData(null, 13)]
    [InlineData(null, null)]
    public async Task AddAssetType_ThenReadFromAFreshProvider_RoundTripsTheZoomRange(int? min, int? max)
    {
        var assetType = new AssetType { Name = $"Zoom {min}-{max}", MinZoom = min, MaxZoom = max };
        try
        {
            await using (var writer = CreateSut(fixture.CreateOptions()))
                writer.AddAssetType(assetType);

            await using var reader = CreateSut(fixture.CreateOptions());
            var reloaded = reader.GetAssetTypes().Single(t => t.Id == assetType.Id);

            reloaded.MinZoom.Should().Be(min);
            reloaded.MaxZoom.Should().Be(max);
        }
        finally
        {
            await using var cleanup = CreateSut(fixture.CreateOptions());
            cleanup.DeleteAssetType(assetType.Id);
        }
    }

    [Fact]
    public async Task SeededAssetTypes_HaveNoZoomRange()
    {
        await using var sut = CreateSut(fixture.CreateOptions());

        var seeded = sut.GetAssetTypes().Where(t => t.IsBuiltIn).ToList();

        seeded.Should().NotBeEmpty();
        seeded.Should().OnlyContain(t => t.MinZoom == null && t.MaxZoom == null);
    }

    [Fact]
    public async Task Migration_AppliedByTheFixture_AddedTwoNullableIntegerColumns()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());

        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(ZoomMigration);
        (await ZoomColumnsAsync(db)).Should().Equal("MaxZoom:integer:YES", "MinZoom:integer:YES");
    }

    [Fact]
    public async Task Migration_DownDropsTheColumns_AndUpRestoresThem()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        var migrator = db.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(PreviousMigration);
            (await ZoomColumnsAsync(db)).Should().BeEmpty();
        }
        finally
        {
            await migrator.MigrateAsync(ZoomMigration);
        }

        (await ZoomColumnsAsync(db)).Should().Equal("MaxZoom:integer:YES", "MinZoom:integer:YES");
    }

    [Fact]
    public void Migration_Up_OnlyAddsTheTwoColumns_AndDownOnlyDropsThem()
    {
        var up = new MigrationBuilder(activeProvider: null);
        var down = new MigrationBuilder(activeProvider: null);
        var migration = new ExposedMigration();

        migration.Up(up);
        migration.Down(down);

        up.Operations.Should().AllBeOfType<AddColumnOperation>();
        up.Operations.Cast<AddColumnOperation>()
            .Select(o => (o.Table, o.Name, o.ClrType, o.IsNullable, o.DefaultValue))
            .Should().BeEquivalentTo(new[]
            {
                ("asset_type", "MinZoom", typeof(int), true, (object?)null),
                ("asset_type", "MaxZoom", typeof(int), true, (object?)null),
            });
        down.Operations.Should().AllBeOfType<DropColumnOperation>();
        down.Operations.Cast<DropColumnOperation>().Select(o => (o.Table, o.Name))
            .Should().BeEquivalentTo(new[] { ("asset_type", "MinZoom"), ("asset_type", "MaxZoom") });
    }

    private static async Task<List<string>> ZoomColumnsAsync(GeoAssetsDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name || ':' || data_type || ':' || is_nullable
            FROM information_schema.columns
            WHERE table_name = 'asset_type' AND column_name IN ('MinZoom', 'MaxZoom')
            ORDER BY column_name
            """;
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));
        return columns;
    }
}
