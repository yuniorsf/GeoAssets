using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static GeoAssets.Tools.SharedUseImport.Tests.SharedUseTestData;

namespace GeoAssets.Tools.SharedUseImport.Tests;

public sealed class SharedUseImportApplicationTests : IDisposable
{
    private const string Connection = "Host=unused";
    private const string ValidMapping = """{ "poles": [], "occupants": [], "organizations": [], "wireTypes": [] }""";

    private readonly string _directory = Directory.CreateTempSubdirectory("shared-use-import-").FullName;
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryPoleOccupancyStore _store;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<SharedUseImportOptions> _opened = [];
    private int _disposals;

    public SharedUseImportApplicationTests() => _store = new InMemoryPoleOccupancyStore(new FixedTenantContext(Owner), _clock);

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);

    private string PathOf(string name) => Path.Combine(_directory, name);

    private string Write(string name, string content)
    {
        File.WriteAllText(PathOf(name), content);
        return PathOf(name);
    }

    private SharedUseImportApplication Application() => new(
        options =>
        {
            _opened.Add(options);
            var view = _store.ForOrganization(options.OrganizationId);
            return new OccupancyStoreSession(view, view, new CountingDisposable(() => _disposals++));
        },
        _clock,
        _output,
        _error);

    private Task<int> Run(string sharedUses, string mapping, params string[] extra) =>
        Application().RunAsync(
            ["--shared-uses", sharedUses, "--mapping", mapping, "--organization", Owner.ToString(), "--report", PathOf("report.json"), .. extra],
            Connection,
            CancellationToken.None);

    [Fact]
    public async Task RunAsync_SampleFiles_ImportEveryMappedRow_WriteTheReport_AndPrintASummary()
    {
        var exitCode = await Run(Sample("shared-uses.sample.csv"), Sample("mapping.sample.json"));

        exitCode.Should().Be(SharedUseImportApplication.Succeeded);
        _opened.Should().ContainSingle().Which.Should().Be(new SharedUseImportOptions(
            Sample("shared-uses.sample.csv"), Sample("mapping.sample.json"), PathOf("report.json"), Owner, DryRun: false, Connection));
        _disposals.Should().Be(1);
        _output.ToString().Should().Be(
            $"Import: 11 rows read, 3 skipped, 8 imported, 6 flagged; 4 poles, 4 imported, 3 irregular. Report: {PathOf("report.json")}{Environment.NewLine}");
        _error.ToString().Should().BeEmpty();

        var overCapacity = await _store.GetForPoleAsync("sample-pole-0002", CancellationToken.None);
        overCapacity!.Entries.Should().HaveCount(7);
        overCapacity.OverCapacityEntries.Should().ContainSingle();
        overCapacity.Entries.Select(entry => (entry.CableDiameterMm, entry.CableLoadDaN)).Distinct()
            .Should().BeEquivalentTo(new[] { (10.123m, 2.5m), (14.001m, 3.000m) });

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(PathOf("report.json")));
        var root = report.RootElement;
        root.GetProperty("dryRun").GetBoolean().Should().BeFalse();
        root.GetProperty("totals").GetProperty("skippedRowsByReason").GetProperty("DuplicateRow").GetInt32().Should().Be(1);
        root.GetProperty("skippedRows").EnumerateArray().Select(row => row.GetProperty("reason").GetString())
            .Should().Equal("UnmappedPole", "NonPositiveCount", "DuplicateRow");
        var poles = root.GetProperty("poles").EnumerateArray().ToList();
        poles.Select(pole => pole.GetProperty("poleFeatureId").GetString())
            .Should().Equal("sample-pole-0001", "sample-pole-0002", "sample-pole-0003", "sample-pole-0004");
        poles.Select(pole => pole.GetProperty("class").GetString()).Should().Equal("Unidentified", "Unidentified", "Unidentified", "Low");
        poles[1].GetProperty("flags").EnumerateArray().Select(flag => flag.GetString()).Should().Equal("OverCapacity", "UnidentifiedOccupant");
        poles[1].GetProperty("overCapacityEntries").GetInt32().Should().Be(1);
        poles.Should().OnlyContain(pole => pole.GetProperty("outcome").GetString() == "Created");
    }

    [Fact]
    public async Task RunAsync_SameFilesTwice_SecondRunWritesNothing()
    {
        await Run(Sample("shared-uses.sample.csv"), Sample("mapping.sample.json"));
        var stored = _store.Snapshot();
        _clock.Advance(TimeSpan.FromDays(1));

        var exitCode = await Run(Sample("shared-uses.sample.csv"), Sample("mapping.sample.json"));

        exitCode.Should().Be(SharedUseImportApplication.Succeeded);
        _store.Snapshot().Should().BeEquivalentTo(stored);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(PathOf("report.json")));
        report.RootElement.GetProperty("poles").EnumerateArray()
            .Should().OnlyContain(pole => pole.GetProperty("outcome").GetString() == "Unchanged");
    }

    [Fact]
    public async Task RunAsync_DryRun_WritesTheReportButNoOccupancy()
    {
        var exitCode = await Run(Sample("shared-uses.sample.csv"), Sample("mapping.sample.json"), "--dry-run");

        exitCode.Should().Be(SharedUseImportApplication.Succeeded);
        _store.Snapshot().Should().BeEmpty();
        _output.ToString().Should().StartWith("Dry run: 11 rows read, 3 skipped, 0 imported");
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(PathOf("report.json")));
        report.RootElement.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_BadArguments_PrintsTheErrorAndUsage_AndOpensNothing()
    {
        var exitCode = await Application().RunAsync(["--bogus"], Connection, CancellationToken.None);

        exitCode.Should().Be(SharedUseImportApplication.UsageError);
        _error.ToString().Should().Be($"Unknown argument '--bogus'.{Environment.NewLine}{SharedUseImportOptions.Usage}{Environment.NewLine}");
        _opened.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing.csv", "mapping.json")]
    [InlineData("shared-uses.csv", "missing.json")]
    [InlineData("no-such-folder/shared-uses.csv", "mapping.json")]
    public async Task RunAsync_MissingInputFile_IsAnInputError_AndOpensNothing(string sharedUses, string mapping)
    {
        Write("shared-uses.csv", "Id,PoleId,OccupantId,WireTypeId,Count\n");
        Write("mapping.json", ValidMapping);

        var exitCode = await Run(PathOf(sharedUses), PathOf(mapping));

        exitCode.Should().Be(SharedUseImportApplication.InputError);
        _error.ToString().Should().NotBeEmpty();
        _opened.Should().BeEmpty();
        File.Exists(PathOf("report.json")).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_MalformedSharedUses_IsAnInputError()
    {
        var exitCode = await Run(Write("shared-uses.csv", "Id,PoleId,OccupantId,WireTypeId,Count\n1,2,3\n"), Write("mapping.json", ValidMapping));

        exitCode.Should().Be(SharedUseImportApplication.InputError);
        _error.ToString().Should().StartWith("Line 2 has 3 fields");
        _opened.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{", "The mapping file is not valid*")]
    [InlineData("null", "The mapping file is empty (null).*")]
    [InlineData("""{ "poles": [], "occupants": [], "organizations": [] }""", "The mapping file is not valid*")]
    [InlineData("""{ "poles": [], "occupants": [], "organizations": [], "wireTypes": [], "extra": 1 }""", "The mapping file is not valid*")]
    [InlineData("""{ "poles": [{ "legacyPoleId": 1, "poleFeatureId": null }], "occupants": [], "organizations": [], "wireTypes": [] }""", "The mapping file is not valid*")]
    [InlineData("""{ "poles": [{ "legacyPoleId": 1 }], "occupants": [], "organizations": [], "wireTypes": [] }""", "The mapping file is not valid*")]
    [InlineData("""{ "poles": [{ "legacyPoleId": 1, "poleFeatureId": "a" }, { "legacyPoleId": 1, "poleFeatureId": "b" }], "occupants": [], "organizations": [], "wireTypes": [] }""", "Legacy pole 1 is mapped more than once.*")]
    public async Task RunAsync_InvalidMapping_IsAnInputError(string mapping, string expectedError)
    {
        var exitCode = await Run(Write("shared-uses.csv", "Id,PoleId,OccupantId,WireTypeId,Count\n"), Write("mapping.json", mapping));

        exitCode.Should().Be(SharedUseImportApplication.InputError);
        _error.ToString().Should().Match(expectedError);
        _opened.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_OccupancyWrittenByTheRun_BelongsToTheOrganizationOnTheCommandLine()
    {
        await Run(Sample("shared-uses.sample.csv"), Sample("mapping.sample.json"));

        _store.Snapshot().Should().OnlyContain(pole => pole.PoleOwnerOrganizationId == Owner);
        (await _store.ForOrganization(OccupantOrganization).GetForPoleAsync("sample-pole-0001", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        using var writer = new StringWriter();

        var nullStore = () => new SharedUseImportApplication(null!, _clock, writer, writer);
        var nullClock = () => new SharedUseImportApplication(_ => null!, null!, writer, writer);
        var nullOutput = () => new SharedUseImportApplication(_ => null!, _clock, null!, writer);
        var nullError = () => new SharedUseImportApplication(_ => null!, _clock, writer, null!);

        nullStore.Should().Throw<ArgumentNullException>().WithParameterName("openStore");
        nullClock.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        nullOutput.Should().Throw<ArgumentNullException>().WithParameterName("output");
        nullError.Should().Throw<ArgumentNullException>().WithParameterName("error");
    }

    private sealed class CountingDisposable(Action onDispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            onDispose();
            return ValueTask.CompletedTask;
        }
    }
}
