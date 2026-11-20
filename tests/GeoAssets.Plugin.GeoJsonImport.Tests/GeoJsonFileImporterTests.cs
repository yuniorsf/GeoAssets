using Xunit;
using FluentAssertions;
using GeoAssets.Core.Models;

namespace GeoAssets.Plugin.GeoJsonImport.Tests;

public sealed class GeoJsonFileImporterTests : IDisposable
{
    private readonly TempGeoJsonFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public async Task ImportAsync_FeaturesInFile_LandInTargetAndCountIsReturned()
    {
        var path = _files.Write([TempGeoJsonFiles.Feature("a"), TempGeoJsonFiles.Feature("b")]);
        var target = new RecordingAssetProvider();

        var count = await GeoJsonFileImporter.ImportAsync(path, target, CancellationToken.None);

        count.Should().Be(2);
        target.GetAll().Select(f => f.Id).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task ImportAsync_DuplicateFeatureIds_LastWinsAndCountedOnce()
    {
        var path = _files.Write([TempGeoJsonFiles.Feature("a", "first"), TempGeoJsonFiles.Feature("b"), TempGeoJsonFiles.Feature("a", "last")]);
        var target = new RecordingAssetProvider();

        var count = await GeoJsonFileImporter.ImportAsync(path, target, CancellationToken.None);

        count.Should().Be(2);
        target.GetById("a")!.Properties.Name.Should().Be("last");
    }

    [Fact]
    public async Task ImportAsync_CustomAssetTypes_AddedBeforeFeatures()
    {
        var typeId = Guid.NewGuid();
        var path = _files.Write([TempGeoJsonFiles.Feature("a")], [TempGeoJsonFiles.CustomType(typeId)]);
        var target = new RecordingAssetProvider();

        await GeoJsonFileImporter.ImportAsync(path, target, CancellationToken.None);

        target.Operations.Should().Equal($"type:{typeId}", "range:1");
        target.GetAssetTypes().Should().Contain(t => t.Id == typeId);
    }

    [Fact]
    public async Task ImportAsync_BuiltInAssetTypes_AreSkipped()
    {
        var path = _files.Write([TempGeoJsonFiles.Feature("a")]);
        var target = new RecordingAssetProvider();

        await GeoJsonFileImporter.ImportAsync(path, target, CancellationToken.None);

        target.Operations.Should().Equal("range:1");
    }

    [Fact]
    public async Task ImportAsync_DuplicateCustomAssetTypes_AddedOnce()
    {
        var typeId = Guid.NewGuid();
        var path = _files.Write(
            [TempGeoJsonFiles.Feature("a")],
            [TempGeoJsonFiles.CustomType(typeId, "first"), TempGeoJsonFiles.CustomType(typeId, "second")]);
        var target = new RecordingAssetProvider();

        await GeoJsonFileImporter.ImportAsync(path, target, CancellationToken.None);

        target.Operations.Count(o => o.StartsWith("type:", StringComparison.Ordinal)).Should().Be(1);
        target.GetAssetTypes().Single(t => t.Id == typeId).Name.Should().Be("first");
    }

    [Fact]
    public async Task ImportAsync_EmptyCollection_ReturnsZeroAndAddsNoFeatures()
    {
        var path = _files.Write([]);
        var target = new RecordingAssetProvider();

        var count = await GeoJsonFileImporter.ImportAsync(path, target, CancellationToken.None);

        count.Should().Be(0);
        target.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_InvalidJson_ThrowsInvalidOperationNamingTheFile()
    {
        var path = _files.WriteRaw("null");
        var target = new RecordingAssetProvider();

        var act = () => GeoJsonFileImporter.ImportAsync(path, target, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(path);
        target.Operations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public Task ImportAsync_NullOrBlankPath_ThrowsArgumentException(string? path)
    {
        var act = () => GeoJsonFileImporter.ImportAsync(path!, new RecordingAssetProvider(), CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public Task ImportAsync_NullTarget_ThrowsArgumentNullException()
    {
        var act = () => GeoJsonFileImporter.ImportAsync(_files.Write([]), null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ImportAsync_CancelledBeforeReading_ThrowsOperationCanceled()
    {
        var path = _files.Write([TempGeoJsonFiles.Feature("a")]);
        var target = new RecordingAssetProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => GeoJsonFileImporter.ImportAsync(path, target, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        target.Operations.Should().BeEmpty();
    }
}
