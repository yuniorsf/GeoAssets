using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.Deployment;

/// <summary>
/// XD01-469: the runtime images carry no <c>libgssapi_krb5.so.2</c>, so Npgsql's default GSS encryption probe
/// (<c>GSS Encryption Mode=Prefer</c>) wrote <c>Cannot load library libgssapi_krb5.so.2</c> to every PostgreSQL host's
/// container log at startup. Every host image whose project references Npgsql turns the probe off in its runtime stage
/// with <c>PGGSSENCMODE=disable</c>, the libpq variable Npgsql reads as that setting's default.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class PostgresHostImageGssEncryptionTests
{
    private const string DisableGssEncryption = "ENV PGGSSENCMODE=disable";

    [Fact]
    public void EveryPostgresHostImage_DisablesGssEncryptionInItsRuntimeStage()
    {
        var hosts = PostgresHostImages().ToList();

        hosts.Select(h => h.Host).Should().Contain(
            ["GeoAssets.Server", "GeoAssets.NeutralOperator.Server", "GeoAssets.FieldService.Server"],
            "the kernel and the vertical hosts with an image all reach PostgreSQL through Npgsql");
        foreach (var (host, dockerfile) in hosts)
        {
            DisablesGssEncryptionInRuntimeStage(dockerfile).Should().BeTrue(
                "{0}'s runtime image has no libgssapi_krb5.so.2 for Npgsql's GSS encryption probe", host);
        }
    }

    [Fact]
    public void AnImageWithoutNpgsql_IsNotRequiredToDisableGssEncryption()
    {
        PostgresHostImages().Select(h => h.Host).Should().NotContain("GeoAssets.Gateway");
    }

    [Theory]
    [InlineData("FROM build AS runtime\nENV PGGSSENCMODE=disable\nENTRYPOINT [\"dotnet\"]", true)]
    [InlineData("FROM sdk AS build\nENV PGGSSENCMODE=disable\nFROM aspnet AS runtime\nENTRYPOINT [\"dotnet\"]", false)]
    [InlineData("FROM aspnet AS runtime\nENV PGGSSENCMODE=prefer", false)]
    public void TheRuntimeStage_IsTheLastStage(string dockerfile, bool disablesInRuntime)
    {
        ArgumentNullException.ThrowIfNull(dockerfile);

        DisablesGssEncryptionInRuntimeStage(dockerfile).Should().Be(disablesInRuntime);
    }

    private static IEnumerable<(string Host, string Dockerfile)> PostgresHostImages()
    {
        var apps = Path.Combine(FindRepositoryRoot(), "apps");
        foreach (var dockerfile in Directory.EnumerateFiles(apps, "Dockerfile", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var directory = Path.GetDirectoryName(dockerfile)!;
            var host = Path.GetFileName(directory);
            var project = Path.Combine(directory, $"{host}.csproj");
            if (File.Exists(project) && ReferencesNpgsql(project))
                yield return (host, File.ReadAllText(dockerfile));
        }
    }

    private static bool ReferencesNpgsql(string project) =>
        XDocument.Load(project).Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Any(id => id is not null && id.StartsWith("Npgsql", StringComparison.Ordinal));

    private static bool DisablesGssEncryptionInRuntimeStage(string dockerfile)
    {
        var lines = dockerfile.Split('\n').Select(l => l.Trim()).ToList();
        var lastFrom = lines.FindLastIndex(l => l.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase));
        return lines.Skip(Math.Max(lastFrom, 0)).Any(l => string.Equals(l, DisableGssEncryption, StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln not found above the test output directory.");
    }
}
