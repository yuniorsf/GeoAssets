using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Tools.NetworkImport.Tests.Support;
using Xunit;

namespace GeoAssets.Tools.NetworkImport.Tests;

public sealed class VerifyCommandTests
{
    private const string Organization = "0000000a-0000-0000-0000-000000000001";

    private static GeoFeature Feature(string id, string assetType) => new()
    {
        Id = id,
        Geometry = new GeoPoint(-38.5, -12.97),
        Properties = new GeoFeatureProperties { Name = id, AssetTypeId = assetType },
    };

    private static async Task<string> WriteFeaturesAsync(TempDirectory directory, params GeoFeature[] features)
    {
        var path = directory.File("features.ndjson");
        var compact = GeoJsonSerializer.CompactOptions;
        await File.WriteAllLinesAsync(path, features.Select(f => JsonSerializer.Serialize(f, compact)));
        return path;
    }

    private static string[] VerifyArgs(string features, params string[] extra) =>
        ["verify", "--kernel-url", "http://kernel.test", "--organization", Organization, "--features", features, .. extra];

    [Fact]
    public async Task Verify_AllFeaturesPresentWithTheSameAssetType_ExitsZero()
    {
        using var temp = new TempDirectory();
        var planned = new[] { Feature("f-1", "type-a"), Feature("f-2", "type-b") };
        var handler = new FakeKernelHandler { FeatureLookup = id => planned.Single(f => string.Equals(f.Id, id, StringComparison.Ordinal)) };

        var result = await CliRunner.RunAsync(handler, VerifyArgs(await WriteFeaturesAsync(temp, planned)));

        result.ExitCode.Should().Be(0, result.Output);
        handler.FeatureGets.Select(r => r.Uri.AbsolutePath).Should().BeEquivalentTo(
            [$"/api/geoassets/organizations/{Organization}/features/f-1", $"/api/geoassets/organizations/{Organization}/features/f-2"]);
    }

    [Fact]
    public async Task Verify_AClassImportMappedOntoAnExistingTypeByRole_ExpectsThatType()
    {
        using var temp = new TempDirectory();
        var lvSegment = NetworkImportInputs.Profile("neoenergia-gse").AssetClasses.Single(c => string.Equals(c.Name, "LvSegment", StringComparison.Ordinal));
        var seeded = new AssetType { Id = Guid.Parse("00000000-0000-0000-0000-000000000006"), Name = "Low tension wire", Roles = [lvSegment.Role] };
        var planned = new[] { Feature("lv-1", lvSegment.TypeId.ToString()) };
        var handler = new FakeKernelHandler { FeatureLookup = id => Feature(id, seeded.Id.ToString()) };
        handler.ExistingAssetTypes.Add(seeded);

        var result = await CliRunner.RunAsync(handler, VerifyArgs(await WriteFeaturesAsync(temp, planned)));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Verified 1 of 1");
    }

    [Fact]
    public async Task Verify_AClassStoredUnderItsOwnTypeInsteadOfTheReusedOne_IsAMismatch()
    {
        using var temp = new TempDirectory();
        var lvSegment = NetworkImportInputs.Profile("neoenergia-gse").AssetClasses.Single(c => string.Equals(c.Name, "LvSegment", StringComparison.Ordinal));
        var seeded = new AssetType { Id = Guid.Parse("00000000-0000-0000-0000-000000000006"), Name = "Low tension wire", Roles = [lvSegment.Role] };
        var planned = new[] { Feature("lv-1", lvSegment.TypeId.ToString()) };
        var handler = new FakeKernelHandler { FeatureLookup = id => Feature(id, lvSegment.TypeId.ToString()) };
        handler.ExistingAssetTypes.Add(seeded);

        var result = await CliRunner.RunAsync(handler, VerifyArgs(await WriteFeaturesAsync(temp, planned)));

        result.ExitCode.Should().Be(3);
        result.Output.Should().Contain($"expected {seeded.Id}");
    }

    [Fact]
    public async Task Verify_SendsTheBearerTokenAndTenantHeader()
    {
        using var temp = new TempDirectory();
        var planned = new[] { Feature("f-1", "type-a") };
        var handler = new FakeKernelHandler { FeatureLookup = id => planned.Single(f => string.Equals(f.Id, id, StringComparison.Ordinal)) };

        await CliRunner.RunAsync(handler, VerifyArgs(await WriteFeaturesAsync(temp, planned), "--tenant-domain", "coelba.example"));

        handler.Requests.Should().OnlyContain(r => string.Equals(r.Headers["Authorization"], "Bearer " + CliRunner.Token, StringComparison.Ordinal));
        handler.Requests.Should().OnlyContain(r => string.Equals(r.Headers["X-Tenant-Domain"], "coelba.example", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_AMissingFeature_ExitsWithThreeAndNamesIt()
    {
        using var temp = new TempDirectory();
        var planned = new[] { Feature("f-1", "type-a"), Feature("f-missing", "type-a") };
        var handler = new FakeKernelHandler { FeatureLookup = id => string.Equals(id, "f-1", StringComparison.Ordinal) ? planned[0] : null };

        var result = await CliRunner.RunAsync(handler, VerifyArgs(await WriteFeaturesAsync(temp, planned)));

        result.ExitCode.Should().Be(3);
        result.Output.Should().Contain("f-missing");
    }

    [Fact]
    public async Task Verify_ADifferentAssetType_ExitsWithThreeAndNamesTheMismatch()
    {
        using var temp = new TempDirectory();
        var planned = new[] { Feature("f-1", "type-a") };
        var handler = new FakeKernelHandler { FeatureLookup = id => Feature(id, "type-other") };

        var result = await CliRunner.RunAsync(handler, VerifyArgs(await WriteFeaturesAsync(temp, planned)));

        result.ExitCode.Should().Be(3);
        result.Output.Should().Contain("f-1");
    }

    [Fact]
    public async Task Verify_SampleOption_ChecksOnlyTheFirstNFeatures()
    {
        using var temp = new TempDirectory();
        var planned = Enumerable.Range(1, 10).Select(i => Feature($"f-{i}", "type-a")).ToArray();
        var handler = new FakeKernelHandler { FeatureLookup = id => planned.Single(f => string.Equals(f.Id, id, StringComparison.Ordinal)) };

        var result = await CliRunner.RunAsync(handler, VerifyArgs(await WriteFeaturesAsync(temp, planned), "--sample", "3"));

        result.ExitCode.Should().Be(0, result.Output);
        handler.FeatureGets.Should().HaveCount(3);
    }

    [Fact]
    public async Task Verify_DefaultSample_IsFifty()
    {
        using var temp = new TempDirectory();
        var planned = Enumerable.Range(1, 60).Select(i => Feature($"f-{i}", "type-a")).ToArray();
        var handler = new FakeKernelHandler { FeatureLookup = id => planned.Single(f => string.Equals(f.Id, id, StringComparison.Ordinal)) };

        var result = await CliRunner.RunAsync(handler, VerifyArgs(await WriteFeaturesAsync(temp, planned)));

        result.ExitCode.Should().Be(0, result.Output);
        handler.FeatureGets.Should().HaveCount(50);
    }
}
