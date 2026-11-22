using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Providers;

/// <summary>
/// XD01-330 — the generic <see cref="OrganizationCatalogReferenceFeatureQuery"/> over the organization catalogs: only the
/// named organization's own features in the box, projected to the five reference fields, narrowed by type and scope.
/// </summary>
public sealed class OrganizationCatalogReferenceFeatureQueryTests
{
    private static readonly Guid Coelba = Guid.Parse("c0e1ba00-0000-4000-8000-000000000330");
    private static readonly Guid Bimbo = Guid.Parse("b1b00000-0000-4000-8000-000000000330");

    private static readonly BoundingBox Box = new(0, 0, 10, 10);
    private static readonly ReferenceFeatureFilter Everything = new(null, null, int.MaxValue);

    private readonly TestAssetProvider _catalog = new();

    private OrganizationCatalogReferenceFeatureQuery Sut() => new(new FilteringOrganizationCatalogFactory(_catalog));

    private void Seed(string id, Guid owner, GeoGeometry? geometry, string? assetTypeId = null, string? scopeId = null)
    {
        var properties = new GeoFeatureProperties { Name = $"Label {id}", OrganizationId = owner };
        if (assetTypeId is not null) properties.AssetTypeId = assetTypeId;
        if (scopeId is not null) properties.CustomAttributes["scopeId"] = scopeId;
        properties.CustomAttributes["secret"] = "never leaves the server";
        _catalog.Add(new GeoFeature { Id = id, Geometry = geometry, Properties = properties });
    }

    private static async Task<IReadOnlyList<string>> IdsAsync(Task<IReadOnlyList<ReferenceFeature>> query) =>
        [.. (await query).Select(f => f.Id)];

    [Fact]
    public async Task Returns_only_the_organizations_own_features_with_a_geometry_in_the_box()
    {
        Seed("own-in", Coelba, new GeoPoint(5, 5));
        Seed("own-edge", Coelba, new GeoPoint(10, 5));
        Seed("own-out", Coelba, new GeoPoint(20, 20));
        Seed("own-no-geometry", Coelba, null);
        Seed("shared", Guid.Empty, new GeoPoint(5, 5));
        Seed("other", Bimbo, new GeoPoint(5, 5));

        (await IdsAsync(Sut().GetInBoundsAsync(Coelba, Box, Everything, CancellationToken.None)))
            .Should().Equal("own-edge", "own-in");
    }

    [Fact]
    public async Task Projects_id_geometry_label_and_the_asset_type_from_the_organizations_catalog()
    {
        var own = new AssetType { Id = Guid.NewGuid(), Name = "COELBA pole", OrganizationId = Coelba };
        var foreign = new AssetType { Id = Guid.NewGuid(), Name = "BIMBO pole", OrganizationId = Bimbo };
        _catalog.AddAssetType(own);
        _catalog.AddAssetType(foreign);
        Seed("a", Coelba, new GeoPoint(1, 2), own.Id.ToString());
        Seed("b", Coelba, new GeoPoint(3, 4), AssetType.Point.Id.ToString());
        Seed("c", Coelba, new GeoPoint(5, 6), foreign.Id.ToString());
        Seed("d", Coelba, new GeoPoint(7, 8), "not-a-known-type");

        var result = await Sut().GetInBoundsAsync(Coelba, Box, Everything, CancellationToken.None);

        result.Select(f => (f.Id, f.Label, f.AssetTypeName)).Should().Equal(
            ("a", "Label a", "COELBA pole"),
            ("b", "Label b", AssetType.Point.Name),
            ("c", "Label c", string.Empty),
            ("d", "Label d", string.Empty));
        result[0].Geometry.Should().BeOfType<GeoPoint>().Which.Coordinates.Should().Equal(1, 2);
        result[0].AssetTypeId.Should().Be(own.Id.ToString());
    }

    [Fact]
    public async Task An_asset_type_filter_keeps_that_type_only()
    {
        Seed("pole", Coelba, new GeoPoint(1, 1), "type-pole");
        Seed("box", Coelba, new GeoPoint(2, 2), "type-box");

        (await IdsAsync(Sut().GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter("type-pole", null, int.MaxValue), CancellationToken.None)))
            .Should().Equal("pole");
        (await IdsAsync(Sut().GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(string.Empty, null, int.MaxValue), CancellationToken.None)))
            .Should().Equal("box", "pole");
    }

    [Fact]
    public async Task Scopes_keep_the_features_of_those_scopes_only()
    {
        Seed("m1", Coelba, new GeoPoint(1, 1), scopeId: "municipio-1");
        Seed("m2", Coelba, new GeoPoint(2, 2), scopeId: "municipio-2");
        Seed("unscoped", Coelba, new GeoPoint(3, 3));
        Seed("m1-case", Coelba, new GeoPoint(4, 4), scopeId: "MUNICIPIO-1");

        (await IdsAsync(Sut().GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(null, ["municipio-1"], int.MaxValue), CancellationToken.None)))
            .Should().Equal("m1");
        (await IdsAsync(Sut().GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(null, [], int.MaxValue), CancellationToken.None)))
            .Should().BeEmpty();
        (await IdsAsync(Sut().GetInBoundsAsync(Coelba, Box, Everything, CancellationToken.None)))
            .Should().Equal("m1", "m1-case", "m2", "unscoped");
    }

    [Fact]
    public async Task MaxResults_cuts_after_ordinal_id_ordering()
    {
        Seed("c", Coelba, new GeoPoint(1, 1));
        Seed("a", Coelba, new GeoPoint(2, 2));
        Seed("B", Coelba, new GeoPoint(3, 3));

        (await IdsAsync(Sut().GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(null, null, 2), CancellationToken.None)))
            .Should().Equal("B", "a");
    }

    [Fact]
    public async Task Guards()
    {
        var sut = Sut();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await sut.Invoking(s => s.GetInBoundsAsync(Coelba, Box, null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await sut.Invoking(s => s.GetInBoundsAsync(Coelba, Box, new ReferenceFeatureFilter(null, null, 0), CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        await sut.Invoking(s => s.GetInBoundsAsync(Coelba, Box, Everything, cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}
