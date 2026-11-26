using System.Net;
using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using Xunit;
using static GeoAssets.Server.Tests.FeatureImport.FeatureImportRequests;

namespace GeoAssets.Server.Tests.FeatureImport;

[Collection(FeatureImportHost.CollectionName)]
public sealed class FeatureImportCompletionEndpointTests
{
    private const string Scope = "net/POSTE";
    private const string OtherScope = "net/INSTALACAO";

    private static GeoPoint Point() => new(-38.5, -12.97);

    private static async Task<(FeatureImportHostContext Host, Guid Org, string Type)> ArrangeAsync()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        return (host, org, type);
    }

    [Fact]
    public async Task Complete_ReplaceScope_RemovesOnlyTheScopesRowsFromEarlierImports()
    {
        var (host, org, type) = await ArrangeAsync();
        var kept = NewId();
        var stale = new[] { NewId(), NewId() };
        var otherScope = NewId();
        using (await PostChunkAsync(host, org, "run-1", Scope, [Feature(kept, type, Point()), .. stale.Select(id => Feature(id, type, Point()))]))
        {
        }

        using (await PostChunkAsync(host, org, "run-1", OtherScope, [Feature(otherScope, type, Point())]))
        {
        }

        using (await PostChunkAsync(host, org, "run-2", Scope, [Feature(kept, type, Point())]))
        {
        }

        using var response = await CompleteAsync(host, org, "run-2", Scope, "replaceScope");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadCompletionAsync(response)).Removed.Should().Be(2);
        (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, kept)).Should().NotBeNull();
        (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, otherScope)).Should().NotBeNull();
        foreach (var id in stale)
        {
            (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, id)).Should().BeNull();
            (await FeatureImportDatabase.GetAttributeRowsAsync(host.ConnectionString, id)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Complete_ReplaceScope_NeverTouchesAnotherOrganizationsRowsInTheSameScope()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var neighbour = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        var neighbourType = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, neighbour, GeometryType.Point);
        var mine = NewId();
        var theirs = NewId();
        using (await PostChunkAsync(host, org, "run-1", Scope, [Feature(mine, type, Point())]))
        {
        }

        using (await PostChunkAsync(host, neighbour, "run-1", Scope, [Feature(theirs, neighbourType, Point())]))
        {
        }

        using var response = await CompleteAsync(host, org, "run-2", Scope, "replaceScope");

        (await ReadCompletionAsync(response)).Removed.Should().Be(1);
        (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, mine)).Should().BeNull();
        (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, theirs)).Should().NotBeNull();
    }

    [Fact]
    public async Task Complete_Merge_RemovesNothing()
    {
        var (host, org, type) = await ArrangeAsync();
        var ids = new[] { NewId(), NewId() };
        using (await PostChunkAsync(host, org, "run-1", Scope, ids.Select(id => Feature(id, type, Point()))))
        {
        }

        using var response = await CompleteAsync(host, org, "run-2", Scope, "merge");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadCompletionAsync(response)).Removed.Should().Be(0);
        (await FeatureImportDatabase.CountEntitiesAsync(host.ConnectionString, org)).Should().Be(2);
    }

    [Fact]
    public async Task Complete_ReplaceScopeOnAnEmptyScope_RemovesNothing()
    {
        var (host, org, _) = await ArrangeAsync();

        using var response = await CompleteAsync(host, org, "run-1", "net/NOTHING", "replaceScope");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadCompletionAsync(response)).Removed.Should().Be(0);
    }

    [Fact]
    public async Task Complete_ReplaceScope_KeepsRowsTheSameImportWrote()
    {
        var (host, org, type) = await ArrangeAsync();
        var id = NewId();
        using (await PostChunkAsync(host, org, "run-1", Scope, [Feature(id, type, Point())]))
        {
        }

        using var response = await CompleteAsync(host, org, "run-1", Scope, "replaceScope");

        (await ReadCompletionAsync(response)).Removed.Should().Be(0);
        (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, id)).Should().NotBeNull();
    }

    [Theory]
    [InlineData("bad%20id")]
    [InlineData("bad%2Fid")]
    public async Task Complete_InvalidImportId_Returns400WithTheInvalidImportIdCode(string importId)
    {
        var (host, org, _) = await ArrangeAsync();

        using var response = await CompleteAsync(host, org, importId, Scope, "merge");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.invalid-import-id");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Complete_BlankScope_Returns400WithTheInvalidScopeCode(string scope)
    {
        var (host, org, _) = await ArrangeAsync();

        using var response = await CompleteAsync(host, org, "run-1", scope, "merge");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.invalid-scope");
    }

    [Fact]
    public async Task Complete_ScopeLongerThan256Characters_Returns400WithTheInvalidScopeCode()
    {
        var (host, org, _) = await ArrangeAsync();

        using var response = await CompleteAsync(host, org, "run-1", new string('s', 257), "merge");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.invalid-scope");
    }

    [Fact]
    public async Task Complete_MissingScope_Returns400WithTheInvalidScopeCode()
    {
        var (host, org, _) = await ArrangeAsync();

        using var response = await PostCompleteAsync(host, org, "run-1", "{\"mode\":\"merge\"}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.invalid-scope");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"scope\":\"net/POSTE\",\"mode\":\"wipe\"}")]
    [InlineData("")]
    public async Task Complete_UnreadableBodyOrUnknownMode_Returns400(string body)
    {
        var (host, org, _) = await ArrangeAsync();

        using var response = await PostCompleteAsync(host, org, "run-1", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
