using FluentAssertions;
using GeoAssets.Tools.NetworkImport.Tests.Support;
using Xunit;

namespace GeoAssets.Tools.NetworkImport.Tests;

public sealed class NetworkImportCommandLineTests
{
    private const string Organization = "0000000a-0000-0000-0000-000000000001";
    private const string Profile = "neoenergia-gse";

    public static TheoryData<string[]> UsageErrors => new()
    {
        Array.Empty<string>(),
        new[] { "bogus" },
        new[] { "--root", "x" },
        new[] { "inventory" },
        new[] { "inventory", "--root", "somewhere" },
        new[] { "inventory", "--profile", Profile },
        new[] { "inventory", "--root", "somewhere", "--profile", Profile, "--bogus", "value" },
        new[] { "inventory", "--root" },
        new[] { "inventory", "--root", "somewhere", "--root", "again", "--profile", Profile },
        new[] { "plan", "--root", "somewhere", "--profile", Profile },
        new[] { "plan", "--root", "somewhere", "--scope-prefix", "coelba" },
        new[] { "plan", "--profile", Profile, "--scope-prefix", "coelba" },
        new[] { "import", "--root", "somewhere", "--profile", Profile, "--scope-prefix", "coelba", "--organization", Organization },
        new[] { "import", "--root", "somewhere", "--profile", Profile, "--scope-prefix", "coelba", "--kernel-url", "http://kernel" },
        new[] { "import", "--profile", Profile, "--scope-prefix", "coelba", "--kernel-url", "http://kernel", "--organization", Organization },
        new[] { "verify", "--kernel-url", "http://kernel", "--organization", Organization },
        new[] { "verify", "--features", "f.ndjson", "--organization", Organization },
        new[] { "verify", "--features", "f.ndjson", "--kernel-url", "http://kernel" },
        new[] { "import", "--root", "somewhere", "--profile", Profile, "--scope-prefix", "coelba", "--kernel-url", "http://kernel", "--organization", Organization, "--export", "geoparquet" },
        new[] { "plan", "--root", "somewhere", "--profile", Profile, "--scope-prefix", "coelba", "--export" },
    };

    [Theory]
    [MemberData(nameof(UsageErrors))]
    public async Task Run_UsageError_ExitsWithOneAndExplainsOnStderr(string[] args)
    {
        var result = await CliRunner.RunAsync(args);

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("plan")]
    public async Task Run_RootDirectoryThatDoesNotExist_ExitsWithTwo(string verb)
    {
        using var temp = new TempDirectory();
        var args = string.Equals(verb, "plan", StringComparison.Ordinal)
            ? new[] { verb, "--root", temp.File("missing"), "--profile", Profile, "--scope-prefix", "coelba", "--out", temp.Path }
            : [verb, "--root", temp.File("missing"), "--profile", Profile, "--out", temp.Path];

        var result = await CliRunner.RunAsync(args);

        result.ExitCode.Should().Be(2);
        result.Stderr.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("plan")]
    public async Task Run_UnknownProfile_ExitsWithTwo(string verb)
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var args = string.Equals(verb, "plan", StringComparison.Ordinal)
            ? new[] { verb, "--root", delivery.Root, "--profile", "no-such-profile", "--scope-prefix", "coelba", "--out", output.Path }
            : [verb, "--root", delivery.Root, "--profile", "no-such-profile", "--out", output.Path];

        var result = await CliRunner.RunAsync(args);

        result.ExitCode.Should().Be(2);
        result.Stderr.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Run_ImportWithAnInvalidOrganizationGuid_ExitsWithTwoWithoutCallingTheKernel()
    {
        using var delivery = new CoelbaDelivery();
        using var output = new TempDirectory();
        var handler = new FakeKernelHandler();

        var result = await CliRunner.RunAsync(
            handler, "import", "--root", delivery.Root, "--profile", Profile, "--scope-prefix", "coelba",
            "--kernel-url", "http://kernel.test", "--organization", "not-a-guid", "--out", output.Path);

        result.ExitCode.Should().Be(2);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_VerifyWithAnInvalidOrganizationGuid_ExitsWithTwo()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(temp.File("features.ndjson"), string.Empty);

        var result = await CliRunner.RunAsync(
            "verify", "--kernel-url", "http://kernel.test", "--organization", "nope", "--features", temp.File("features.ndjson"));

        result.ExitCode.Should().Be(2);
    }

    [Fact]
    public async Task Run_VerifyWithAMissingFeaturesFile_ExitsWithTwo()
    {
        using var temp = new TempDirectory();

        var result = await CliRunner.RunAsync(
            "verify", "--kernel-url", "http://kernel.test", "--organization", Organization, "--features", temp.File("absent.ndjson"));

        result.ExitCode.Should().Be(2);
    }
}
