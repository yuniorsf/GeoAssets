using Xunit;
using FluentAssertions;
using GeoAssets.Commands.Contracts;

namespace GeoAssets.Plugin.GeoJsonImport.Tests;

public sealed class GeoJsonImportHandlerTests : IDisposable
{
    private readonly TempGeoJsonFiles _files = new();
    private readonly RecordingAssetProvider _repository = new();
    private readonly GeoJsonImportHandler _sut = new();

    public void Dispose() => _files.Dispose();

    private static Dictionary<string, object> NewParameters() => new(StringComparer.Ordinal);

    private GeoCommandContext Context() => new(_repository);

    [Fact]
    public void ExportGeoCommand_Attribute_IsGeoJsonImportFileInImportCategory()
    {
        var attribute = typeof(GeoJsonImportHandler)
            .GetCustomAttributes(typeof(ExportGeoCommandAttribute), inherit: false)
            .Cast<ExportGeoCommandAttribute>()
            .Single();

        attribute.Name.Should().Be("geojson-import-file");
        attribute.Category.Should().Be("Import");
    }

    [Fact]
    public async Task ExecuteAsync_ValidFile_WritesToContextRepositoryAndReturnsOk()
    {
        var path = _files.Write([TempGeoJsonFiles.Feature("a"), TempGeoJsonFiles.Feature("b")]);

        var result = await _sut.ExecuteAsync(Context(), new Dictionary<string, object>(StringComparer.Ordinal) { ["filePath"] = path }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repository.GetAll().Should().HaveCount(2);
        var data = result.Data!;
        data.GetType().GetProperty("imported")!.GetValue(data).Should().Be(2);
        data.GetType().GetProperty("source")!.GetValue(data).Should().Be(path);
    }

    [Fact]
    public async Task ExecuteAsync_ConnectionStringPresent_FailsNamingParameterAndWritesNothing()
    {
        var path = _files.Write([TempGeoJsonFiles.Feature("a")]);
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["filePath"] = path, ["connectionString"] = "Host=x" };

        var result = await _sut.ExecuteAsync(Context(), parameters, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("connectionString");
        _repository.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_FilePathMissing_Fails()
    {
        var result = await _sut.ExecuteAsync(Context(), NewParameters(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("filePath");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ExecuteAsync_FilePathBlank_Fails(string value)
    {
        var result = await _sut.ExecuteAsync(Context(), new Dictionary<string, object>(StringComparer.Ordinal) { ["filePath"] = value }, CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_FilePathNotAString_Fails()
    {
        var result = await _sut.ExecuteAsync(Context(), new Dictionary<string, object>(StringComparer.Ordinal) { ["filePath"] = 42 }, CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_FileNotFound_Fails()
    {
        var result = await _sut.ExecuteAsync(Context(), new Dictionary<string, object>(StringComparer.Ordinal) { ["filePath"] = _files.MissingPath() }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_UnparseableFile_FailsWithImportFailedPrefix()
    {
        var path = _files.WriteRaw("{ not json");

        var result = await _sut.ExecuteAsync(Context(), new Dictionary<string, object>(StringComparer.Ordinal) { ["filePath"] = path }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith("Import failed:");
        _repository.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_CancelledToken_PropagatesOperationCanceled()
    {
        var path = _files.Write([TempGeoJsonFiles.Feature("a")]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _sut.ExecuteAsync(Context(), new Dictionary<string, object>(StringComparer.Ordinal) { ["filePath"] = path }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public Task ExecuteAsync_NullContext_ThrowsArgumentNullException()
    {
        var act = () => _sut.ExecuteAsync(null!, NewParameters(), CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public Task ExecuteAsync_NullParameters_ThrowsArgumentNullException()
    {
        var act = () => _sut.ExecuteAsync(Context(), null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }
}
