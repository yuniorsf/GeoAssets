using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Entities;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Confirms the geo_entity_attribute-based GetPageAsync (XD01-171) returns identical results to
/// the original jsonb_each_text-based implementation it replaced, for a fixed set of queries
/// against the same seeded data — identical ids, identical order, identical TotalCount.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderGetPageAsyncParityTests(PostgresContainerFixture fixture)
{
    private const string PoleTypeId = "type-pole";
    private const string TransformerTypeId = "type-transformer";
    private const string SwitchTypeId = "type-switch";

    private async Task ClearAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity CASCADE");
    }

    private static PostgresAssetProvider CreateSut(DbContextOptions<GeoAssetsDbContext> options) =>
        new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);

    /// <summary>
    /// Near-verbatim copy of GetPageAsync's jsonb_each_text-based SearchText matching as it
    /// existed before XD01-171's fix — test-only scaffolding kept purely for comparison, never
    /// shipped. Returns just ids + TotalCount since that's all the parity assertions need.
    /// </summary>
    private static async Task<(List<string> Ids, int TotalCount)> LegacyGetPageAsync(GeoAssetsDbContext db, AssetQuery query)
    {
        IQueryable<GeoEntityRow> rows = db.GeoEntities.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.AssetTypeId))
            rows = rows.Where(r => r.AssetTypeId == query.AssetTypeId);

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var pattern = $"%{query.SearchText}%";

            var matchingAttributeIds = await db.Database
                .SqlQuery<string>($"""
                    SELECT DISTINCT ge."Id"
                    FROM geo_entity ge, jsonb_each_text(ge.custom_attributes) AS attr(key, value)
                    WHERE attr.key ILIKE {pattern} OR attr.value ILIKE {pattern}
                    """)
                .ToListAsync();

            rows = rows.Where(r =>
                EF.Functions.ILike(r.Name, pattern) ||
                EF.Functions.ILike(r.Description, pattern) ||
                matchingAttributeIds.Contains(r.Id));
        }

        var totalCount = await rows.CountAsync();

        rows = query.SortBy switch
        {
            "name"      => query.SortDescending ? rows.OrderByDescending(r => r.Name)      : rows.OrderBy(r => r.Name),
            "createdAt" => query.SortDescending ? rows.OrderByDescending(r => r.CreatedAt) : rows.OrderBy(r => r.CreatedAt),
            "updatedAt" => query.SortDescending ? rows.OrderByDescending(r => r.UpdatedAt) : rows.OrderBy(r => r.UpdatedAt),
            _           => query.SortDescending ? rows.OrderByDescending(r => r.Id)        : rows.OrderBy(r => r.Id)
        };

        var ids = await rows.Skip(query.Skip).Take(query.Take).Select(r => r.Id).ToListAsync();
        return (ids, totalCount);
    }

    private static List<GeoFeature> BuildSeedFeatures()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var features = new List<GeoFeature>();

        for (var i = 1; i <= 300; i++)
        {
            var typeId = (i % 3) switch { 0 => PoleTypeId, 1 => TransformerTypeId, _ => SwitchTypeId };
            var material = (i % 5) switch { 0 => "concreto", 1 => "madera", _ => "metal" };
            var customAttributes = new Dictionary<string, string>
            {
                ["material"] = material,
                ["serie"] = $"SN-{i:D4}",
            };
            if (i == 42)
                customAttributes["codigo_especial"] = "unico";

            features.Add(new GeoFeature
            {
                Id = $"parity-{i:D4}",
                Properties = new GeoFeatureProperties
                {
                    Name = $"Asset {i:D4}",
                    AssetTypeId = typeId,
                    Description = $"Parity test asset number {i}",
                    CreatedAt = baseTime.AddSeconds(i),
                    UpdatedAt = baseTime.AddSeconds(301 - i),
                    CustomAttributes = customAttributes,
                }
            });
        }

        return features;
    }

    private static readonly AssetQuery[] FixedQueries =
    [
        new AssetQuery { SearchText = "Asset 0042" },
        new AssetQuery { SearchText = "SN-0042" },
        new AssetQuery { SearchText = "codigo_especial" },
        new AssetQuery { SearchText = "zzz-no-match" },
        new AssetQuery { SearchText = "concreto" },
        new AssetQuery { SortBy = "name" },
        new AssetQuery { SortBy = "createdAt" },
        new AssetQuery { SortBy = "updatedAt" },
        new AssetQuery { SortBy = "name", SortDescending = true },
        new AssetQuery { Skip = 250, Take = 50 },
        new AssetQuery { AssetTypeId = PoleTypeId },
        new AssetQuery { SearchText = "concreto", AssetTypeId = PoleTypeId, SortBy = "createdAt", Skip = 10, Take = 20 },
    ];

    [Fact]
    public async Task GetPageAsync_MatchesLegacyJsonbEachTextImplementation_ForFixedQueries()
    {
        await ClearAsync();

        await using var sut = CreateSut(fixture.CreateOptions());
        sut.AddRange(BuildSeedFeatures());

        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());

        foreach (var query in FixedQueries)
        {
            var (legacyIds, legacyTotal) = await LegacyGetPageAsync(db, query);
            var result = await sut.GetPageAsync(query);

            result.Items.Select(f => f.Id).Should().Equal(legacyIds,
                because: $"SearchText='{query.SearchText}' SortBy='{query.SortBy}' Skip={query.Skip} AssetTypeId='{query.AssetTypeId}' should return identical ids in identical order");
            result.TotalCount.Should().Be(legacyTotal,
                because: $"SearchText='{query.SearchText}' SortBy='{query.SortBy}' AssetTypeId='{query.AssetTypeId}' should have identical TotalCount");
        }
    }
}
