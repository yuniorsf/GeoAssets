using System.Net;
using System.Text.Json;
using System.Web;
using FluentAssertions;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Tools.NetworkImport.Tests.Support;
using Xunit;

namespace GeoAssets.Tools.NetworkImport.Tests;

public sealed class ImportCommandTests
{
    private const string Organization = "0000000a-0000-0000-0000-000000000001";
    private const string ExistingPoleTypeId = "99999999-0000-0000-0000-000000000001";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string[] ImportArgs(CoelbaDelivery delivery, TempDirectory output, params string[] extra) =>
        [
            "import", "--root", delivery.Root, "--profile", "neoenergia-gse", "--scope-prefix", "coelba",
            "--kernel-url", "http://kernel.test", "--organization", Organization, "--out", output.Path, .. extra,
        ];

    private static async Task<(CliResult Result, FakeKernelHandler Handler)> ImportAsync(
        Action<FakeKernelHandler>? configure = null, params string[] extra)
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var handler = new FakeKernelHandler();
        configure?.Invoke(handler);
        var result = await CliRunner.RunAsync(handler, ImportArgs(delivery, output, extra));
        return (result, handler);
    }

    private static string ScopeOf(RecordedRequest request) =>
        HttpUtility.ParseQueryString(request.Uri.Query)["scope"]!;

    [Fact]
    public async Task Import_Succeeds_AndPostsOneChunkPerScopeToTheOrganizationsRoute()
    {
        var (result, handler) = await ImportAsync();

        result.ExitCode.Should().Be(0, result.Output);
        var chunks = handler.Chunks.ToArray();
        chunks.Select(ScopeOf).Should().BeEquivalentTo(["coelba/POSTE", "coelba/INSTALACAO", "coelba/REDE_MT"]);
        chunks.Should().OnlyContain(c => c.Uri.AbsolutePath.StartsWith($"/api/geoassets/organizations/{Organization}/feature-imports/", StringComparison.Ordinal));
        chunks.Should().OnlyContain(c => string.Equals(c.Uri.Host, "kernel.test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_SendsGzippedNdjsonWhoseFeaturesAllBelongToTheChunksScope()
    {
        var (_, handler) = await ImportAsync();

        foreach (var chunk in handler.Chunks)
        {
            chunk.WasGzip.Should().BeTrue();
            chunk.ContentType.Should().Be("application/x-ndjson");
            var features = chunk.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonSerializer.Deserialize<GeoFeature>(line, GeoJsonSerializer.Options)!).ToArray();
            features.Should().NotBeEmpty();
            features.Select(f => f.Properties.CustomAttributes[FeatureImportAttributes.Scope]).Distinct(StringComparer.Ordinal)
                .Should().Equal(ScopeOf(chunk));
        }
    }

    [Fact]
    public async Task Import_SendsTheBearerTokenFromTheEnvironment()
    {
        var (_, handler) = await ImportAsync();

        handler.Requests.Should().NotBeEmpty();
        handler.Requests.Should().OnlyContain(r => string.Equals(r.Headers["Authorization"], "Bearer " + CliRunner.Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_TenantDomainOption_SetsTheTenantHeaderOnEveryRequest()
    {
        var (_, handler) = await ImportAsync(extra: ["--tenant-domain", "coelba.example"]);

        handler.Requests.Should().OnlyContain(r => string.Equals(r.Headers["X-Tenant-Domain"], "coelba.example", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_WithoutTenantDomain_SendsNoTenantHeader()
    {
        var (_, handler) = await ImportAsync();

        handler.Requests.Should().OnlyContain(r => !r.Headers.ContainsKey("X-Tenant-Domain"));
    }

    [Fact]
    public async Task Import_EnsuresAssetTypes_PostingEveryClassTheKernelLacks()
    {
        var (_, handler) = await ImportAsync();

        var posted = handler.AssetTypePosts.Select(r => JsonSerializer.Deserialize<AssetType>(r.Body, GeoJsonSerializer.Options)!).ToArray();
        posted.Should().HaveCount(15);
        var transformer = posted.Single(t => t.Id == Guid.Parse("81f4acdf-6d53-490d-b894-a9b41a71d18c"));
        transformer.Name.Should().Be("Transformador");
        transformer.Color.Should().Be("#f9a825");
        transformer.AllowedGeometryType.Should().Be(GeometryType.Point);
        transformer.Roles.Should().Equal("electric:transformer");
        posted.Single(t => t.Roles.Contains("electric:mv-segment", StringComparer.Ordinal)).AllowedGeometryType.Should().Be(GeometryType.LineString);
    }

    [Fact]
    public async Task Import_ReusesAnExistingTypeThatCarriesTheClassesReuseRole()
    {
        var (_, handler) = await ImportAsync(h => h.ExistingAssetTypes.Add(new AssetType
        {
            Id = Guid.Parse(ExistingPoleTypeId),
            Name = "Poste",
            Roles = ["occupiable-support", "isp:pole"],
        }));

        var posted = handler.AssetTypePosts.Select(r => JsonSerializer.Deserialize<AssetType>(r.Body, GeoJsonSerializer.Options)!).ToArray();
        posted.Should().HaveCount(14);
        posted.Should().NotContain(t => t.Roles.Contains("electric:pole"));
        var poleFeatures = handler.Chunks.Where(c => string.Equals(ScopeOf(c), "coelba/POSTE", StringComparison.Ordinal))
            .SelectMany(c => c.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .Select(l => JsonSerializer.Deserialize<GeoFeature>(l, GeoJsonSerializer.Options)!)
            .Where(f => f.Properties.Name.StartsWith("Poste", StringComparison.Ordinal));
        poleFeatures.Should().OnlyContain(f => string.Equals(f.Properties.AssetTypeId, ExistingPoleTypeId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_ReusesAnExistingTypeThatAlreadyCarriesTheClassRole()
    {
        var (_, handler) = await ImportAsync(h => h.ExistingAssetTypes.Add(new AssetType
        {
            Id = Guid.Parse(ExistingPoleTypeId),
            Name = "Transformador existente",
            Roles = ["electric:transformer"],
        }));

        handler.AssetTypePosts.Should().HaveCount(14);
        handler.AssetTypePosts.Select(r => r.Body).Should().NotContain(b => b.Contains("electric:transformer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_CompletesEachScopeOnceInMergeModeAfterTheLastChunk()
    {
        var (_, handler) = await ImportAsync();

        var completions = handler.Completions.ToArray();
        completions.Should().HaveCount(3);
        completions.Should().OnlyContain(c => c.Uri.AbsolutePath.StartsWith($"/api/geoassets/organizations/{Organization}/feature-imports/", StringComparison.Ordinal));
        var bodies = completions.Select(c => JsonSerializer.Deserialize<JsonElement>(c.Body, Web)).ToArray();
        bodies.Select(b => b.GetProperty("scope").GetString()).Should().BeEquivalentTo(["coelba/POSTE", "coelba/INSTALACAO", "coelba/REDE_MT"]);
        bodies.Should().OnlyContain(b => string.Equals(b.GetProperty("mode").GetString(), "merge", StringComparison.Ordinal));
        var lastChunk = handler.Requests.ToList().FindLastIndex(r => r.Uri.AbsolutePath.EndsWith("/chunks", StringComparison.Ordinal));
        var firstComplete = handler.Requests.ToList().FindIndex(r => r.Uri.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal));
        firstComplete.Should().BeGreaterThan(lastChunk);
    }

    [Fact]
    public async Task Import_ReplaceScopeOption_CompletesWithReplaceScope()
    {
        var (_, handler) = await ImportAsync(extra: "--replace-scope");

        handler.Completions.Select(c => JsonSerializer.Deserialize<JsonElement>(c.Body, Web).GetProperty("mode").GetString())
            .Should().OnlyContain(mode => string.Equals(mode, "replaceScope", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_ImportIdOption_AppearsInEveryImportRoute()
    {
        var (_, handler) = await ImportAsync(extra: ["--import-id", "coelba-run-7"]);

        handler.Chunks.Concat(handler.Completions).Should().OnlyContain(r => r.Uri.AbsolutePath.Contains("/feature-imports/coelba-run-7/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_DefaultImportId_IsTheProfileAndTheInjectedClockTimestamp()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var handler = new FakeKernelHandler();

        await CliRunner.RunAsync(
            handler,
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(CliRunner.Start),
            ImportArgs(delivery, output),
            advanceClock: false);

        handler.Chunks.Should().OnlyContain(c => c.Uri.AbsolutePath.Contains("/feature-imports/neoenergia-gse-20261126102030/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_WritesAReportJson()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();

        var result = await CliRunner.RunAsync(new FakeKernelHandler(), ImportArgs(delivery, output));

        result.ExitCode.Should().Be(0, result.Output);
        File.Exists(output.File("report.json")).Should().BeTrue();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output.File("report.json")));
        document.RootElement.GetProperty("featuresWritten").GetInt64().Should().Be(5);
    }

    [Fact]
    public async Task Import_AssetTypeListingFailing_ExitsWithThree()
    {
        var (result, handler) = await ImportAsync(h => h.AssetTypeListStatus = HttpStatusCode.InternalServerError);

        result.ExitCode.Should().Be(3);
        handler.Chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ANonRetryableChunkFailure_ExitsWithThreeAfterOneAttempt()
    {
        var (result, handler) = await ImportAsync(h => h.ChunkStatus = _ => HttpStatusCode.BadRequest);

        result.ExitCode.Should().Be(3);
        handler.Chunks.Should().ContainSingle();
    }
}
