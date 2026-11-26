using System.Net;
using System.Text.Json;
using System.Web;
using FluentAssertions;
using GeoAssets.Tools.NetworkImport.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Tools.NetworkImport.Tests;

public sealed class ImportRetryAndCheckpointTests
{
    private const string Organization = "0000000a-0000-0000-0000-000000000001";

    private static string[] ImportArgs(CoelbaDelivery delivery, TempDirectory output, params string[] extra) =>
        [
            "import", "--root", delivery.Root, "--profile", "neoenergia-gse", "--scope-prefix", "coelba",
            "--kernel-url", "http://kernel.test", "--organization", Organization, "--out", output.Path, .. extra,
        ];

    private static string ScopeOf(RecordedRequest request) => HttpUtility.ParseQueryString(request.Uri.Query)["scope"]!;

    [Fact]
    public async Task Import_ServiceUnavailableOnce_IsRetriedAndSucceeds()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var handler = new FakeKernelHandler { ChunkStatus = attempt => attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK };

        var result = await CliRunner.RunAsync(handler, new FakeTimeProvider(CliRunner.Start), ImportArgs(delivery, output));

        result.ExitCode.Should().Be(0, result.Output);
        handler.Chunks.Should().HaveCount(4);
        handler.Chunks.Take(2).Select(ScopeOf).Distinct(StringComparer.Ordinal).Should().ContainSingle();
    }

    [Fact]
    public async Task Import_TooManyRequests_IsRetried()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var handler = new FakeKernelHandler { ChunkStatus = attempt => attempt == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK };

        var result = await CliRunner.RunAsync(handler, ImportArgs(delivery, output));

        result.ExitCode.Should().Be(0, result.Output);
    }

    [Fact]
    public async Task Import_ServiceUnavailableOnEveryAttempt_GivesUpAfterThreeAndExitsWithThree()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var handler = new FakeKernelHandler { ChunkStatus = _ => HttpStatusCode.ServiceUnavailable };

        var result = await CliRunner.RunAsync(handler, ImportArgs(delivery, output));

        result.ExitCode.Should().Be(3);
        handler.Chunks.Should().HaveCount(3);
        handler.Completions.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_RetriesWaitOnTheInjectedClock_NotOnWallTime()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var handler = new FakeKernelHandler { ChunkStatus = attempt => attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK };
        var clock = new FakeTimeProvider(CliRunner.Start);

        var result = await CliRunner.RunAsync(handler, clock, ImportArgs(delivery, output));

        result.ExitCode.Should().Be(0, result.Output);
        clock.GetUtcNow().Should().BeAfter(CliRunner.Start);
    }

    [Fact]
    public async Task Import_Checkpoint_IsWrittenWithTheImportIdAndTheLastAcknowledgedSequence()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var checkpoint = output.File("checkpoint.json");

        var result = await CliRunner.RunAsync(
            new FakeKernelHandler(), ImportArgs(delivery, output, "--import-id", "run-1", "--checkpoint", checkpoint));

        result.ExitCode.Should().Be(0, result.Output);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(checkpoint));
        document.RootElement.GetProperty("importId").GetString().Should().Be("run-1");
        document.RootElement.GetProperty("lastSequence").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Import_ResumeWithTheSameImportId_SkipsAcknowledgedChunks()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var checkpoint = output.File("checkpoint.json");
        string[] args = ImportArgs(delivery, output, "--import-id", "run-1", "--checkpoint", checkpoint);
        var failing = new FakeKernelHandler { ChunkStatus = attempt => attempt == 3 ? HttpStatusCode.BadRequest : HttpStatusCode.OK };

        var first = await CliRunner.RunAsync(failing, args);
        var resumed = new FakeKernelHandler();
        var second = await CliRunner.RunAsync(resumed, args);

        first.ExitCode.Should().Be(3);
        failing.Chunks.Should().HaveCount(3);
        second.ExitCode.Should().Be(0, second.Output);
        resumed.Chunks.Should().ContainSingle();
        ScopeOf(resumed.Chunks.Single()).Should().Be(ScopeOf(failing.Chunks.Last()));
    }

    [Fact]
    public async Task Import_ResumeAfterACompleteRun_SendsNoChunksAgain()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var checkpoint = output.File("checkpoint.json");
        string[] args = ImportArgs(delivery, output, "--import-id", "run-1", "--checkpoint", checkpoint);
        await CliRunner.RunAsync(new FakeKernelHandler(), args);
        var resumed = new FakeKernelHandler();

        var result = await CliRunner.RunAsync(resumed, args);

        result.ExitCode.Should().Be(0, result.Output);
        resumed.Chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_CheckpointFromAnotherImportId_IsNotUsedToSkipChunks()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var checkpoint = output.File("checkpoint.json");
        await CliRunner.RunAsync(new FakeKernelHandler(), ImportArgs(delivery, output, "--import-id", "run-1", "--checkpoint", checkpoint));
        var other = new FakeKernelHandler();

        var result = await CliRunner.RunAsync(other, ImportArgs(delivery, output, "--import-id", "run-2", "--checkpoint", checkpoint));

        result.ExitCode.Should().Be(0, result.Output);
        other.Chunks.Should().HaveCount(3);
    }
}
