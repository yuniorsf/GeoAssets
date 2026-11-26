using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;
using static GeoAssets.Server.Tests.FeatureImport.FeatureImportRequests;

namespace GeoAssets.Server.Tests.FeatureImport;

[Collection(FeatureImportHost.CollectionName)]
public sealed class FeatureImportChunkEndpointTests
{
    private const string Scope = "net/POSTE";

    [Fact]
    public async Task Chunk_NewFeatures_AreInsertedAndReported()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var first = NewId();
        var second = NewId();

        using var response = await PostChunkAsync(host, org, "run-1", Scope,
            [Feature(first, type, new GeoPoint(-38.5, -12.97)), Feature(second, type, new GeoPoint(-38.6, -12.98))]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadChunkResultAsync(response);
        result.Inserted.Should().Be(2);
        result.Updated.Should().Be(0);
        result.Rejected.Should().BeEmpty();
        (await FeatureImportDatabase.CountEntitiesAsync(host.ConnectionString, org)).Should().Be(2);
    }

    [Fact]
    public async Task Chunk_ResultIsSerializedInCamelCase()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);

        using var response = await PostChunkAsync(host, org, "run-1", Scope, [Feature(NewId(), type, new GeoPoint(-38.5, -12.97))]);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("inserted").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("updated").GetInt32().Should().Be(0);
        document.RootElement.GetProperty("rejected").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Chunk_ResendingTheSameIds_UpdatesInsteadOfInserting()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var ids = new[] { NewId(), NewId() };
        using (await PostChunkAsync(host, org, "run-1", Scope, ids.Select(id => Feature(id, type, new GeoPoint(-38.5, -12.97), name: "old"))))
        {
        }

        using var response = await PostChunkAsync(host, org, "run-2", Scope, ids.Select(id => Feature(id, type, new GeoPoint(-38.7, -12.9), name: "new")));

        var result = await ReadChunkResultAsync(response);
        result.Inserted.Should().Be(0);
        result.Updated.Should().Be(2);
        var entity = (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, ids[0]))!;
        entity.Name.Should().Be("new");
        entity.X.Should().BeApproximately(-38.7, 1e-9);
        (await FeatureImportDatabase.CountEntitiesAsync(host.ConnectionString, org)).Should().Be(2);
    }

    [Fact]
    public async Task Chunk_MixedNewAndExistingIds_CountsEachCorrectly()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var existing = NewId();
        using (await PostChunkAsync(host, org, "run-1", Scope, [Feature(existing, type, new GeoPoint(-38.5, -12.97))]))
        {
        }

        using var response = await PostChunkAsync(host, org, "run-2", Scope,
            [Feature(existing, type, new GeoPoint(-38.5, -12.97)), Feature(NewId(), type, new GeoPoint(-38.6, -12.97))]);

        var result = await ReadChunkResultAsync(response);
        result.Inserted.Should().Be(1);
        result.Updated.Should().Be(1);
    }

    [Fact]
    public async Task Chunk_StoresTheOrganizationAssetTypeNameAndWgs84Geometry()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var id = NewId();

        using var response = await PostChunkAsync(host, org, "run-1", Scope, [Feature(id, type, new GeoPoint(-38.5, -12.97), name: "Poste 1")]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entity = (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, id))!;
        entity.OrganizationId.Should().Be(org);
        entity.AssetTypeId.Should().Be(type);
        entity.Name.Should().Be("Poste 1");
        entity.Srid.Should().Be(4326);
        entity.X.Should().BeApproximately(-38.5, 1e-9);
        entity.Y.Should().BeApproximately(-12.97, 1e-9);
    }

    [Fact]
    public async Task Chunk_StampsTheScopeAndImportIdOnEveryFeature()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var id = NewId();

        using var response = await PostChunkAsync(host, org, "coelba-run-9", "coelba/Norte/POSTE", [Feature(id, type, new GeoPoint(-38.5, -12.97))]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entity = (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, id))!;
        using var custom = JsonDocument.Parse(entity.CustomAttributesJson);
        custom.RootElement.GetProperty(FeatureImportAttributes.Scope).GetString().Should().Be("coelba/Norte/POSTE");
        custom.RootElement.GetProperty(FeatureImportAttributes.ImportId).GetString().Should().Be("coelba-run-9");
    }

    [Fact]
    public async Task Chunk_ASecondImportRestampsTheImportId()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var id = NewId();
        using (await PostChunkAsync(host, org, "run-1", Scope, [Feature(id, type, new GeoPoint(-38.5, -12.97))]))
        {
        }

        using (await PostChunkAsync(host, org, "run-2", Scope, [Feature(id, type, new GeoPoint(-38.5, -12.97))]))
        {
        }

        var entity = (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, id))!;
        using var custom = JsonDocument.Parse(entity.CustomAttributesJson);
        custom.RootElement.GetProperty(FeatureImportAttributes.ImportId).GetString().Should().Be("run-2");
    }

    [Fact]
    public async Task Chunk_WritesTheFeaturesCustomAttributesIntoTheGeoEntityAttributeTable()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var id = NewId();
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["NU_PG_ID"] = "123", ["DE_BARRAMENTO"] = "ABC" };

        using var response = await PostChunkAsync(host, org, "run-1", Scope, [Feature(id, type, new GeoPoint(-38.5, -12.97), attributes: attributes)]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await FeatureImportDatabase.GetAttributeRowsAsync(host.ConnectionString, id);
        rows["NU_PG_ID"].Should().Be("123");
        rows["DE_BARRAMENTO"].Should().Be("ABC");
        rows[FeatureImportAttributes.Scope].Should().Be(Scope);
        rows[FeatureImportAttributes.ImportId].Should().Be("run-1");
    }

    [Fact]
    public async Task Chunk_ResendingReplacesTheAttributeRowsOfTheWrittenIds()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var id = NewId();
        var before = new Dictionary<string, string>(StringComparer.Ordinal) { ["OLD_KEY"] = "1", ["KEPT"] = "a" };
        var after = new Dictionary<string, string>(StringComparer.Ordinal) { ["NEW_KEY"] = "2", ["KEPT"] = "b" };
        using (await PostChunkAsync(host, org, "run-1", Scope, [Feature(id, type, new GeoPoint(-38.5, -12.97), attributes: before)]))
        {
        }

        using (await PostChunkAsync(host, org, "run-2", Scope, [Feature(id, type, new GeoPoint(-38.5, -12.97), attributes: after)]))
        {
        }

        var rows = await FeatureImportDatabase.GetAttributeRowsAsync(host.ConnectionString, id);
        rows.Should().NotContainKey("OLD_KEY");
        rows["NEW_KEY"].Should().Be("2");
        rows["KEPT"].Should().Be("b");
    }

    [Fact]
    public async Task Chunk_PersistsTopologyEdges()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var id = NewId();
        var target = NewId();

        using var response = await PostChunkAsync(host, org, "run-1", Scope, [Feature(id, type, new GeoPoint(-38.5, -12.97), edgeTarget: target)]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entity = (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, id))!;
        using var topology = JsonDocument.Parse(entity.TopologyJson);
        topology.RootElement.EnumerateArray().Single().GetProperty("targetId").GetString().Should().Be(target);
    }

    [Fact]
    public async Task Chunk_AcceptsAGzipRequestBody()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);

        using var response = await PostChunkAsync(
            host, org, "run-1", Scope, [Feature(NewId(), type, new GeoPoint(-38.5, -12.97)), Feature(NewId(), type, new GeoPoint(-38.6, -12.97))], gzip: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadChunkResultAsync(response)).Inserted.Should().Be(2);
    }

    [Fact]
    public async Task Chunk_IgnoresBlankLinesBetweenFeatures()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var body = Ndjson([Feature(NewId(), type, new GeoPoint(-38.5, -12.97))])
            + "\n   \n" + Ndjson([Feature(NewId(), type, new GeoPoint(-38.6, -12.97))]) + "\n";

        using var response = await PostChunkBodyAsync(host, org, "run-1", Scope, body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadChunkResultAsync(response)).Inserted.Should().Be(2);
    }

    [Fact]
    public async Task Chunk_ExactlyTheMaximumChunkSize_IsAccepted()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var features = Enumerable.Range(0, FeatureImportLimits.MaxChunkSize)
            .Select(i => Feature(NewId(), type, new GeoPoint(-38.5 + (i * 1e-5), -12.97)));

        using var response = await PostChunkAsync(host, org, "run-1", Scope, features, gzip: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadChunkResultAsync(response)).Inserted.Should().Be(FeatureImportLimits.MaxChunkSize);
    }
}
