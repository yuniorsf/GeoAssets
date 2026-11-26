using System.Net;
using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models.Geometry;
using Xunit;
using static GeoAssets.Server.Tests.FeatureImport.FeatureImportRequests;

namespace GeoAssets.Server.Tests.FeatureImport;

[Collection(FeatureImportHost.CollectionName)]
public sealed class FeatureImportValidationEndpointTests
{
    private const string Scope = "net/POSTE";

    private static async Task<(FeatureImportHostContext Host, Guid Org, string Type)> ArrangeAsync()
    {
        var host = await FeatureImportHost.GetAsync();
        var org = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, org, GeometryType.Point);
        return (host, org, type);
    }

    [Theory]
    [InlineData("bad%20id")]
    [InlineData("bad%2Fid")]
    [InlineData("caf%C3%A9")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123x")]
    public async Task Chunk_InvalidImportId_Returns400WithTheInvalidImportIdCode(string importId)
    {
        var (host, org, type) = await ArrangeAsync();

        using var response = await PostChunkAsync(host, org, importId, Scope, [Feature(NewId(), type, new GeoPoint(-38.5, -12.97))]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.invalid-import-id");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("coelba-neoenergia_2026.11.26")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123")]
    public async Task Chunk_ValidImportId_IsAccepted(string importId)
    {
        var (host, org, type) = await ArrangeAsync();

        using var response = await PostChunkAsync(host, org, importId, Scope, [Feature(NewId(), type, new GeoPoint(-38.5, -12.97))]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Chunk_MissingScope_Returns400WithTheInvalidScopeCode()
    {
        var (host, org, type) = await ArrangeAsync();

        using var response = await PostChunkAsync(host, org, "run-1", scope: null, [Feature(NewId(), type, new GeoPoint(-38.5, -12.97))]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.invalid-scope");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Chunk_BlankScope_Returns400WithTheInvalidScopeCode(string scope)
    {
        var (host, org, type) = await ArrangeAsync();

        using var response = await PostChunkAsync(host, org, "run-1", scope, [Feature(NewId(), type, new GeoPoint(-38.5, -12.97))]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.invalid-scope");
    }

    [Fact]
    public async Task Chunk_ScopeLongerThan256Characters_Returns400WithTheInvalidScopeCode()
    {
        var (host, org, type) = await ArrangeAsync();

        using var response = await PostChunkAsync(host, org, "run-1", new string('s', 257), [Feature(NewId(), type, new GeoPoint(-38.5, -12.97))]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.invalid-scope");
    }

    [Fact]
    public async Task Chunk_ScopeOfExactly256Characters_IsAccepted()
    {
        var (host, org, type) = await ArrangeAsync();

        using var response = await PostChunkAsync(host, org, "run-1", new string('s', 256), [Feature(NewId(), type, new GeoPoint(-38.5, -12.97))]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Chunk_MalformedLine_Returns400WithTheInvalidFeatureCodeAndWritesNothing()
    {
        var (host, org, type) = await ArrangeAsync();
        var good = NewId();
        var body = Ndjson([Feature(good, type, new GeoPoint(-38.5, -12.97))]) + "{ this is not json\n";

        using var response = await PostChunkBodyAsync(host, org, "run-1", Scope, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.invalid-feature");
        (await FeatureImportDatabase.FindEntityAsync(host.ConnectionString, good)).Should().BeNull();
    }

    [Fact]
    public async Task Chunk_AJsonValueThatIsNotAFeature_Returns400WithTheInvalidFeatureCode()
    {
        var (host, org, _) = await ArrangeAsync();

        using var response = await PostChunkBodyAsync(host, org, "run-1", Scope, "[1,2,3]\n");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.invalid-feature");
    }

    [Fact]
    public async Task Chunk_MoreThanTheMaximumChunkSize_Returns413WithTheChunkTooLargeCode()
    {
        var (host, org, type) = await ArrangeAsync();
        var features = Enumerable.Range(0, FeatureImportLimits.MaxChunkSize + 1)
            .Select(i => Feature(NewId(), type, new GeoPoint(-38.5 + (i * 1e-5), -12.97)));

        using var response = await PostChunkAsync(host, org, "run-1", Scope, features, gzip: true);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await ReadProblemCodeAsync(response)).Should().Be("feature-import.chunk-too-large");
        (await FeatureImportDatabase.CountEntitiesAsync(host.ConnectionString, org)).Should().Be(0);
    }

    [Fact]
    public async Task Chunk_WithoutCredentials_Returns401()
    {
        var (host, org, type) = await ArrangeAsync();

        using var response = await PostChunkAsync(host, org, "run-1", Scope, [Feature(NewId(), type, new GeoPoint(-38.5, -12.97))], roles: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Supervisor,Administrator")]
    [InlineData("SomeOtherRole")]
    public async Task Chunk_WithoutTheServiceCallerRole_Returns403AndWritesNothing(string roles)
    {
        var (host, org, type) = await ArrangeAsync();

        using var response = await PostChunkAsync(host, org, "run-1", Scope, [Feature(NewId(), type, new GeoPoint(-38.5, -12.97))], roles: roles);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await FeatureImportDatabase.CountEntitiesAsync(host.ConnectionString, org)).Should().Be(0);
    }

    [Fact]
    public async Task Complete_WithoutCredentials_Returns401()
    {
        var (host, org, _) = await ArrangeAsync();

        using var response = await PostCompleteAsync(host, org, "run-1", "{\"scope\":\"net/POSTE\",\"mode\":\"merge\"}", roles: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("SomeOtherRole")]
    public async Task Complete_WithoutTheServiceCallerRole_Returns403(string roles)
    {
        var (host, org, _) = await ArrangeAsync();

        using var response = await PostCompleteAsync(host, org, "run-1", "{\"scope\":\"net/POSTE\",\"mode\":\"merge\"}", roles: roles);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
