using System.Net;
using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;
using static GeoAssets.Server.Tests.FeatureImport.FeatureImportRequests;

namespace GeoAssets.Server.Tests.FeatureImport;

[Collection(FeatureImportHost.CollectionName)]
public sealed class FeatureImportRejectionTests
{
    private const string Scope = "net/POSTE";

    private static GeoPoint Point() => new(-38.5, -12.97);

    private static async Task<(FeatureImportChunkResult Result, FeatureImportHostContext Host)> SendAsync(
        Guid org, Func<string, GeoFeature> make, GeometryType allowed = GeometryType.Point)
    {
        var host = await FeatureImportHost.GetAsync();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, allowed);
        using var response = await PostChunkAsync(host, org, "run-1", Scope, [make(type)]);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadChunkResultAsync(response), host);
    }

    [Fact]
    public async Task Chunk_IdOwnedByAnotherOrganization_IsRejectedAsForeignAndLeftUntouched()
    {
        var host = await FeatureImportHost.GetAsync();
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var ownerType = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, owner, GeometryType.Point);
        var intruderType = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, intruder, GeometryType.Point);
        var id = NewId();
        using (await PostChunkAsync(host, owner, "run-1", Scope, [Feature(id, ownerType, Point(), name: "original")]))
        {
        }

        using var response = await PostChunkAsync(host, intruder, "run-1", Scope, [Feature(id, intruderType, Point(), name: "hijacked")]);

        var result = await ReadChunkResultAsync(response);
        result.Inserted.Should().Be(0);
        result.Updated.Should().Be(0);
        result.Rejected.Should().ContainSingle().Which.Should().Be(new FeatureImportRejection(id, FeatureImportRejectionReasons.ForeignOrganization));
        var entity = (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, id))!;
        entity.OrganizationId.Should().Be(owner);
        entity.Name.Should().Be("original");
    }

    [Fact]
    public async Task Chunk_UnknownAssetType_IsRejected()
    {
        var id = NewId();

        var (result, host) = await SendAsync(Guid.NewGuid(), _ => Feature(id, Guid.NewGuid().ToString(), Point()));

        result.Inserted.Should().Be(0);
        result.Rejected.Should().ContainSingle().Which.Should().Be(new FeatureImportRejection(id, FeatureImportRejectionReasons.UnknownAssetType));
        (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, id)).Should().BeNull();
    }

    [Fact]
    public async Task Chunk_AssetTypeOfAnotherOrganization_IsRejectedAsUnknown()
    {
        var host = await FeatureImportHost.GetAsync();
        var otherOrgType = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, Guid.NewGuid(), GeometryType.Point);
        var id = NewId();

        using var response = await PostChunkAsync(host, Guid.NewGuid(), "run-1", Scope, [Feature(id, otherOrgType, Point())]);

        (await ReadChunkResultAsync(response)).Rejected.Should().ContainSingle().Which.Reason.Should().Be(FeatureImportRejectionReasons.UnknownAssetType);
    }

    [Fact]
    public async Task Chunk_BuiltInAssetTypeVisibleToEveryOrganization_IsAccepted()
    {
        var host = await FeatureImportHost.GetAsync();
        var id = NewId();

        using var response = await PostChunkAsync(host, Guid.NewGuid(), "run-1", Scope,
            [Feature(id, "00000000-0000-0000-0000-000000000004", Point())]);

        (await ReadChunkResultAsync(response)).Inserted.Should().Be(1);
    }

    [Fact]
    public async Task Chunk_GeometryOfAnotherKindThanTheTypeAllows_IsRejected()
    {
        var id = NewId();

        var (result, _) = await SendAsync(
            Guid.NewGuid(), type => Feature(id, type, new GeoLineString([(-38.5, -12.97), (-38.6, -12.98)])), GeometryType.Point);

        result.Rejected.Should().ContainSingle().Which.Should().Be(new FeatureImportRejection(id, FeatureImportRejectionReasons.GeometryMismatch));
    }

    [Fact]
    public async Task Chunk_LineGeometryForALineType_IsAccepted()
    {
        var id = NewId();

        var (result, _) = await SendAsync(
            Guid.NewGuid(), type => Feature(id, type, new GeoLineString([(-38.5, -12.97), (-38.6, -12.98)])), GeometryType.LineString);

        result.Inserted.Should().Be(1);
    }

    [Fact]
    public async Task Chunk_FeatureWithoutGeometry_IsRejected()
    {
        var id = NewId();

        var (result, _) = await SendAsync(Guid.NewGuid(), type => Feature(id, type, geometry: null));

        result.Rejected.Should().ContainSingle().Which.Should().Be(new FeatureImportRejection(id, FeatureImportRejectionReasons.MissingGeometry));
    }

    [Fact]
    public async Task Chunk_NameLongerThan256Characters_IsRejected()
    {
        var id = NewId();

        var (result, _) = await SendAsync(Guid.NewGuid(), type => Feature(id, type, Point(), name: new string('n', 257)));

        result.Rejected.Should().ContainSingle().Which.Should().Be(new FeatureImportRejection(id, FeatureImportRejectionReasons.NameTooLong));
    }

    [Fact]
    public async Task Chunk_NameOfExactly256Characters_IsAccepted()
    {
        var id = NewId();

        var (result, _) = await SendAsync(Guid.NewGuid(), type => Feature(id, type, Point(), name: new string('n', 256)));

        result.Inserted.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789012345678901234567890123456789")]
    public async Task Chunk_BlankOrTooLongId_IsRejectedAsInvalid(string id)
    {
        var (result, _) = await SendAsync(Guid.NewGuid(), type => Feature(id, type, Point()));

        result.Inserted.Should().Be(0);
        result.Rejected.Should().ContainSingle().Which.Reason.Should().Be(FeatureImportRejectionReasons.InvalidId);
    }

    [Fact]
    public async Task Chunk_OneRejectedFeatureDoesNotBlockTheValidOnes()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var good = NewId();
        var bad = NewId();

        using var response = await PostChunkAsync(host, org, "run-1", Scope,
            [Feature(good, type, Point()), Feature(bad, type, geometry: null)]);

        var result = await ReadChunkResultAsync(response);
        result.Inserted.Should().Be(1);
        result.Rejected.Select(r => r.FeatureId).Should().Equal(bad);
        (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, good)).Should().NotBeNull();
        (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, bad)).Should().BeNull();
    }
}
