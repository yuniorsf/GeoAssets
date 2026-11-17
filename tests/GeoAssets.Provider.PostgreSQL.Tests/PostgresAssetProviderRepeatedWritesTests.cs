using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// XD01-341: <see cref="PostgresAssetProvider"/>'s long-lived context must not keep the rows it
/// saved tracked — a second <c>Update</c> of a feature and an <c>AddRange</c> upsert of a stored
/// id map fresh row instances for those keys — and an upsert is atomic, its attribute reset
/// included. Each case runs on a context with and without a retrying execution strategy (the
/// hosts' providers use one). Each test uses its own ids and removes them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderRepeatedWritesTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _token = $"rw{Guid.NewGuid():N}"[..12];
    private readonly List<string> _ids = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.GeoEntities.IgnoreQueryFilters().Where(r => _ids.Contains(r.Id)).ExecuteDeleteAsync();
    }

    private PostgresAssetProvider CreateSut(bool retrying)
    {
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql =>
            {
                npgsql.UseNetTopologySuite();
                if (retrying)
                    npgsql.EnableRetryOnFailure();
            })
            .Options;
        return new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
    }

    private GeoFeature Feature(string name, params (string Key, string Value)[] attributes)
    {
        var id = $"{_token}-{_ids.Count}";
        _ids.Add(id);
        return Feature(id, name, attributes);
    }

    private static GeoFeature Feature(string id, string name, params (string Key, string Value)[] attributes) => new()
    {
        Id = id,
        Geometry = new GeoPoint(-38.5, -12.9),
        Properties = new GeoFeatureProperties
        {
            Name = name,
            AssetTypeId = "pole",
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
            CustomAttributes = attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal),
        },
    };

    private async Task<string?> NameAsync(string id)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        return await db.GeoEntities.IgnoreQueryFilters().Where(r => r.Id == id).Select(r => r.Name).SingleOrDefaultAsync();
    }

    private async Task<List<string>> AttributesAsync(string id)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        return await db.GeoEntityAttributes
            .Where(a => a.EntityId == id)
            .OrderBy(a => a.Key)
            .Select(a => a.Key + "=" + a.Value)
            .ToListAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_TwiceOfTheSameFeature_OnOneProvider_PersistsTheLatestValues(bool retrying)
    {
        var feature = Feature("first", ("plate", "P-1"));
        await using var sut = CreateSut(retrying);
        sut.Add(feature);

        sut.Update(Feature(feature.Id, "second", ("plate", "P-2"), ("height", "9")));
        sut.Update(Feature(feature.Id, "third", ("plate", "P-3")));

        (await NameAsync(feature.Id)).Should().Be("third");
        (await AttributesAsync(feature.Id)).Should().Equal("plate=P-3");
        sut.GetById(feature.Id)!.Properties.Name.Should().Be("third");
        sut.GetById(feature.Id)!.Properties.CustomAttributes.Should().Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = "P-3" });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddRange_OfStoredIds_UpdatesThem_AndTheirAttributes_AndInsertsTheNewOnes(bool retrying)
    {
        var stored = Feature("stored", ("plate", "P-1"), ("height", "9"));
        var added = Feature("added", ("plate", "P-9"));
        await using var sut = CreateSut(retrying);
        sut.AddRange([stored]);

        sut.AddRange([Feature(stored.Id, "upserted", ("plate", "P-2")), added]);
        sut.AddRange([Feature(stored.Id, "upserted again", ("plate", "P-3"), ("owner", "x"))]);

        (await NameAsync(stored.Id)).Should().Be("upserted again");
        (await AttributesAsync(stored.Id)).Should().Equal("owner=x", "plate=P-3");
        (await NameAsync(added.Id)).Should().Be("added");
        (await AttributesAsync(added.Id)).Should().Equal("plate=P-9");
        sut.GetById(stored.Id)!.Properties.Name.Should().Be("upserted again");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddRange_ThatFailsToSave_LeavesTheStoredRowsAndAttributesUntouched_AndTheProviderUsable(bool retrying)
    {
        var stored = Feature("stored", ("plate", "P-1"));
        var rejected = Feature(new string('x', 300), ("plate", "P-9"));
        await using var sut = CreateSut(retrying);
        sut.AddRange([stored]);

        var addRange = () => sut.AddRange([Feature(stored.Id, "upserted", ("plate", "P-2")), rejected]);

        addRange.Should().Throw<DbUpdateException>("a name over geo_entity's 256 characters is refused by the database");
        (await NameAsync(stored.Id)).Should().Be("stored");
        (await AttributesAsync(stored.Id)).Should().Equal(new[] { "plate=P-1" }, "the attribute reset is rolled back with the write");
        (await NameAsync(rejected.Id)).Should().BeNull();

        sut.Update(Feature(stored.Id, "updated after the failure", ("plate", "P-3")));

        (await NameAsync(stored.Id)).Should().Be("updated after the failure", "a failed write leaves nothing pending in the provider");
        (await AttributesAsync(stored.Id)).Should().Equal("plate=P-3");
        (await NameAsync(rejected.Id)).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Add_ThenUpdate_ThenDelete_OfTheSameFeature_OnOneProvider_Succeed(bool retrying)
    {
        var feature = Feature("added", ("plate", "P-1"));
        await using var sut = CreateSut(retrying);

        sut.Add(feature);
        sut.Update(Feature(feature.Id, "updated", ("plate", "P-2")));
        (await NameAsync(feature.Id)).Should().Be("updated");

        sut.Delete(feature.Id);

        (await NameAsync(feature.Id)).Should().BeNull();
        (await AttributesAsync(feature.Id)).Should().BeEmpty();
        sut.GetById(feature.Id).Should().BeNull();
    }
}
