using FluentAssertions;
using GeoAssets.Provider.PostgreSQL.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Exercises <see cref="SeedDomainAssetTypesAndLayers.Up"/> directly (no database needed — this
/// just inspects the <see cref="MigrationOperation"/>s it builds) — see XD01-150. Confirms the
/// seeded <c>asset_type</c> rows explicitly carry the "no organization assigned" sentinel
/// (<see cref="Guid.Empty"/>) rather than relying on the column's implicit DB default.
/// </summary>
public sealed class SeedDomainAssetTypesAndLayersMigrationTests
{
    private sealed class ExposedMigration : SeedDomainAssetTypesAndLayers
    {
        public new void Up(MigrationBuilder migrationBuilder) => base.Up(migrationBuilder);
    }

    private static InsertDataOperation RunUpAndGetAssetTypeInsert()
    {
        var builder = new MigrationBuilder(activeProvider: null);
        new ExposedMigration().Up(builder);

        return builder.Operations
            .OfType<InsertDataOperation>()
            .Single(op => op.Table == "asset_type");
    }

    [Fact]
    public void Up_SeedsAssetTypes_WithOrganizationIdExplicitlySetToGuidEmpty()
    {
        var insert = RunUpAndGetAssetTypeInsert();

        var organizationIdColumn = Array.IndexOf(insert.Columns, "OrganizationId");
        organizationIdColumn.Should().BeGreaterThanOrEqualTo(0,
            "the seeded domain asset types are global built-in defaults and must explicitly carry the " +
            "IOrgOwnedResource 'no organization assigned' sentinel instead of relying on an implicit column default");

        for (var row = 0; row < insert.Values.GetLength(0); row++)
            insert.Values[row, organizationIdColumn].Should().Be(Guid.Empty);
    }

    [Fact]
    public void Up_SeedsFiveAssetTypes()
    {
        var insert = RunUpAndGetAssetTypeInsert();

        insert.Values.GetLength(0).Should().Be(5);
    }
}
