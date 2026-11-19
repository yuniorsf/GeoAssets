using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static GeoAssets.Tools.SharedUseImport.Tests.SharedUseTestData;

namespace GeoAssets.Tools.SharedUseImport.Tests;

/// <summary>
/// The tool end to end on a real PostgreSQL (Testcontainers): the store the tool opens
/// (<see cref="PostgresOccupancyStore"/>), the sample input, a second run, and another tenant.
/// </summary>
public sealed class PostgresSharedUseImportTests : IAsyncLifetime, IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("shared-use-import-pg-").FullName;
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        _connectionString = await PostgresOccupancyDatabase.EnsureCreatedAsync();
        await using var db = new ServiceOrderDbContext(new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql(_connectionString).Options);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE neutral_operator.field_pole_occupancies CASCADE;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private Task<int> Run(Guid organization, params string[] extra) =>
        new SharedUseImportApplication(options => PostgresOccupancyStore.Open(options, _clock), _clock, _output, _error).RunAsync(
            [
                "--shared-uses", Path.Combine(AppContext.BaseDirectory, "samples", "shared-uses.sample.csv"),
                "--mapping", Path.Combine(AppContext.BaseDirectory, "samples", "mapping.sample.json"),
                "--organization", organization.ToString(),
                "--report", Path.Combine(_directory, "report.json"),
                .. extra,
            ],
            _connectionString,
            CancellationToken.None);

    private async Task<IReadOnlyList<PoleOccupancy>> ReadAsAsync(Guid organization)
    {
        await using var session = PostgresOccupancyStore.Open(
            new SharedUseImportOptions("", "", "", organization, DryRun: false, _connectionString), _clock);
        return await session.Reader.GetForPolesAsync(
            ["sample-pole-0001", "sample-pole-0002", "sample-pole-0003", "sample-pole-0004"], CancellationToken.None);
    }

    [Fact]
    public async Task Import_WritesEveryPoleThroughTheRealStore_OverCapacityIncluded_AndASecondRunChangesNothing()
    {
        var first = await Run(Owner);
        var stored = await ReadAsAsync(Owner);
        _clock.Advance(TimeSpan.FromHours(2));
        var second = await Run(Owner);

        first.Should().Be(SharedUseImportApplication.Succeeded, _error.ToString());
        second.Should().Be(SharedUseImportApplication.Succeeded, _error.ToString());
        stored.Select(pole => pole.PoleFeatureId).Should().Equal("sample-pole-0001", "sample-pole-0002", "sample-pole-0003", "sample-pole-0004");
        stored.Should().OnlyContain(pole => pole.Version == PoleOccupancy.FirstVersion && pole.PoleOwnerOrganizationId == Owner);
        var overCapacity = stored[1];
        overCapacity.Entries.Should().HaveCount(7);
        overCapacity.RegulatedEntries.Select(entry => entry.SlotIndex).Should().Equal(1, 2, 3, 4, 5, 6);
        overCapacity.OverCapacityEntries.Should().ContainSingle().Which.SlotIndex.Should().BeNull();
        overCapacity.IsOverCapacity.Should().BeTrue();
        overCapacity.Class.Should().Be(OccupancyClass.Unidentified);
        overCapacity.Entries.Should().Contain(entry => entry.CableDiameterMm == 14.001m && entry.CableLoadDaN == 3.000m);
        (await ReadAsAsync(Owner)).Should().BeEquivalentTo(stored, "the second run found every pole unchanged and wrote nothing");
        _output.ToString().Should().Contain("Import: 11 rows read, 3 skipped, 8 imported, 6 flagged; 4 poles, 4 imported, 3 irregular.");
    }

    [Fact]
    public async Task Import_IsScopedToTheOrganizationOnTheCommandLine()
    {
        await Run(Owner);

        (await ReadAsAsync(OccupantOrganization)).Should().BeEmpty();
    }

    [Fact]
    public async Task DryRun_ComparesWithTheRealStore_ButWritesNothing()
    {
        var exitCode = await Run(Owner, "--dry-run");

        exitCode.Should().Be(SharedUseImportApplication.Succeeded, _error.ToString());
        (await ReadAsAsync(Owner)).Should().BeEmpty();
    }
}
