using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-431 — <see cref="TileCacheKey"/> / <see cref="TileCacheScope"/> composition: no two scopes share a tile.</summary>
public class TileCacheKeyTests
{
    private const string OrgA = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
    private const string OrgB = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";
    private static readonly TileAddress Tile = new(12, 1503, 2230);

    private static TileCacheKey Key(
        TileCacheScope? scope = null, string layerSet = "assets", string version = "v7", TileAddress? address = null, TileFormat format = TileFormat.Mvt) =>
        new(scope ?? TileCacheScope.Create(OrgA, ["grant-1"]), layerSet, version, address ?? Tile, format);

    // ── Key ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Value_HoldsEveryPart_InOrder()
    {
        var scope = new TileCacheScope(OrgA, "abc123");

        var key = new TileCacheKey(scope, "assets", "v7", Tile, TileFormat.Mvt);

        key.Value.Should().Be($"tile/{OrgA}/abc123/assets/v7/12/1503/2230.mvt");
        key.ToString().Should().Be(key.Value);
        key.Scope.Should().Be(scope);
        key.LayerSetId.Should().Be("assets");
        key.LayerVersion.Should().Be("v7");
        key.Address.Should().Be(Tile);
        key.Format.Should().Be(TileFormat.Mvt);
        new TileCacheKey(scope, "assets", "v7", Tile, TileFormat.Png).Value.Should().EndWith("/12/1503/2230.png");
    }

    [Fact]
    public void LayerVersionPrefix_StartsEveryKeyOfThatLayerVersion_AndNoOther()
    {
        var scope = new TileCacheScope(OrgA, "abc123");

        var prefix = TileCacheKey.LayerVersionPrefix(scope, "assets", "v7");

        prefix.Should().Be($"tile/{OrgA}/abc123/assets/v7/");
        new TileCacheKey(scope, "assets", "v7", Tile, TileFormat.Png).Value.Should().StartWith(prefix);
        new TileCacheKey(scope, "assets", "v7", new TileAddress(0, 0, 0), TileFormat.Mvt).Value.Should().StartWith(prefix);
        new TileCacheKey(scope, "assets", "v77", Tile, TileFormat.Mvt).Value.Should().NotStartWith(prefix, "the prefix ends at the segment");
        new TileCacheKey(scope, "assets", "v8", Tile, TileFormat.Mvt).Value.Should().NotStartWith(prefix);
        new TileCacheKey(new TileCacheScope(OrgB, "abc123"), "assets", "v7", Tile, TileFormat.Mvt).Value.Should().NotStartWith(prefix);
    }

    [Fact]
    public void LayerVersionPrefix_RefusesWhatAKeyRefuses()
    {
        var scope = new TileCacheScope(OrgA, "abc123");

        FluentActions.Invoking(() => TileCacheKey.LayerVersionPrefix(default, "assets", "v7")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => TileCacheKey.LayerVersionPrefix(scope, "a/b", "v7")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => TileCacheKey.LayerVersionPrefix(scope, "assets", null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Keys_DifferWhenAnyPartDiffers()
    {
        var keys = new[]
        {
            Key(),
            Key(scope: TileCacheScope.Create(OrgB, ["grant-1"])),
            Key(scope: TileCacheScope.Create(OrgA, ["grant-1", "grant-2"])),
            Key(scope: TileCacheScope.Create(OrgA, [])),
            Key(layerSet: "poles"),
            Key(version: "v8"),
            Key(address: new TileAddress(12, 1503, 2231)),
            Key(address: new TileAddress(13, 1503, 2230)),
            Key(format: TileFormat.Png),
        };

        keys.Select(k => k.Value).Should().OnlyHaveUniqueItems();
        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Keys_AreEqualForTheSameParts()
    {
        Key().Should().Be(Key());
        Key().Value.Should().Be(Key().Value);
        Key().GetHashCode().Should().Be(Key().GetHashCode());
    }

    [Fact]
    public void Constructor_RefusesTheDefaultScope()
    {
        var act = () => new TileCacheKey(default, "assets", "v1", Tile, TileFormat.Mvt);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("scope");
    }

    [Theory]
    [InlineData("a/b", "v1", "layerSetId")]
    [InlineData("", "v1", "layerSetId")]
    [InlineData("assets", "v 1", "layerVersion")]
    [InlineData("assets", "..", "layerVersion")]
    public void Constructor_RefusesSegmentsThatCouldCollide(string layerSet, string version, string param)
    {
        var act = () => new TileCacheKey(TileCacheScope.Create(OrgA, []), layerSet, version, Tile, TileFormat.Mvt);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be(param);
    }

    [Fact]
    public void Constructor_RefusesNullSegments()
    {
        var scope = TileCacheScope.Create(OrgA, []);

        var layer = () => new TileCacheKey(scope, null!, "v1", Tile, TileFormat.Mvt);
        var version = () => new TileCacheKey(scope, "assets", null!, Tile, TileFormat.Mvt);

        layer.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("layerSetId");
        version.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("layerVersion");
    }

    [Fact]
    public void Constructor_RefusesAnUndefinedFormat()
    {
        var act = () => Key(format: (TileFormat)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddressTag_IsSharedByEveryScopeVersionAndFormatOfTheTile()
    {
        var tags = new[]
        {
            Key().AddressTag,
            Key(scope: TileCacheScope.Create(OrgB, ["other"])).AddressTag,
            Key(layerSet: "poles", version: "v99", format: TileFormat.Png).AddressTag,
        };

        tags.Should().AllBe("tile-at/12/1503/2230");
        TileCacheKey.AddressTagFor(Tile).Should().Be("tile-at/12/1503/2230");
        Key(address: new TileAddress(12, 1504, 2230)).AddressTag.Should().NotBe(Key().AddressTag);
    }

    // ── Scope & grant hash ──────────────────────────────────────────────────

    [Fact]
    public void Scope_KeepsItsParts()
    {
        var scope = new TileCacheScope(OrgA, "hash");

        scope.OrganizationScope.Should().Be(OrgA);
        scope.GrantScopeHash.Should().Be("hash");
        scope.IsSpecified.Should().BeTrue();
        default(TileCacheScope).IsSpecified.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "h", "organizationScope")]
    [InlineData("org/1", "h", "organizationScope")]
    [InlineData("org", "", "grantScopeHash")]
    [InlineData("org", "h#", "grantScopeHash")]
    public void Scope_RefusesBadSegments(string org, string hash, string param)
    {
        var act = () => new TileCacheScope(org, hash);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be(param);
    }

    [Fact]
    public void HashGrants_DependsOnTheSetAlone()
    {
        var hash = TileCacheScope.HashGrants(["b", "a", "c"]);

        TileCacheScope.HashGrants(["a", "b", "c"]).Should().Be(hash);
        TileCacheScope.HashGrants(["c", "a", "b", "a"]).Should().Be(hash);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void HashGrants_DiffersForDifferentSets()
    {
        var hashes = new[]
        {
            TileCacheScope.HashGrants([]),
            TileCacheScope.HashGrants(["a"]),
            TileCacheScope.HashGrants(["A"]),
            TileCacheScope.HashGrants(["a", "b"]),
            TileCacheScope.HashGrants(["ab"]),
            TileCacheScope.HashGrants(["a", "bc"]),
            TileCacheScope.HashGrants(["ab", "c"]),
            TileCacheScope.HashGrants([""]),
        };

        hashes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void HashGrants_IsStable()
    {
        TileCacheScope.HashGrants([]).Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void HashGrants_RefusesNulls()
    {
        var nullSet = () => TileCacheScope.HashGrants(null!);
        var nullGrant = () => TileCacheScope.HashGrants(["a", null!]);

        nullSet.Should().Throw<ArgumentNullException>();
        nullGrant.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("grants");
    }

    [Fact]
    public void Create_HashesTheGrants()
    {
        var scope = TileCacheScope.Create(OrgA, ["x", "y"]);

        scope.Should().Be(new TileCacheScope(OrgA, TileCacheScope.HashGrants(["y", "x"])));
    }

    // ── Segment rule ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("assets")]
    [InlineData("v1.2_3~4-5")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("...")]
    public void TileKeySegment_AcceptsUnreservedIdentifiers(string value)
    {
        TileKeySegment.IsValid(value).Should().BeTrue();
        var act = () => TileKeySegment.EnsureValid(value, "p");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a b")]
    [InlineData("a%2F")]
    [InlineData("ação")]
    [InlineData("a?b")]
    public void TileKeySegment_RefusesAnythingElse(string? value)
    {
        TileKeySegment.IsValid(value).Should().BeFalse();
    }

    [Fact]
    public void TileKeySegment_RefusesOverlongValues()
    {
        TileKeySegment.IsValid(new string('a', TileKeySegment.MaxLength)).Should().BeTrue();
        TileKeySegment.IsValid(new string('a', TileKeySegment.MaxLength + 1)).Should().BeFalse();
    }

    [Fact]
    public void TileKeySegment_EnsureValid_ThrowsWithTheParameterName()
    {
        var bad = () => TileKeySegment.EnsureValid("a/b", "layer");
        var missing = () => TileKeySegment.EnsureValid(null!, "layer");

        bad.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("layer");
        missing.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("layer");
    }
}
