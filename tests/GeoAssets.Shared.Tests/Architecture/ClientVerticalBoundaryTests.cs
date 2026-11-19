using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Shared.Tests.Architecture;

/// <summary>
/// The client shell names no vertical (XD01-378): <c>GeoAssets.Shared</c> references neither the IpBilling nor the
/// NeutralOperator UI, the hosts reference both libraries and compose them (routes, menu, order-detail components), and
/// no client registers the shared-use order types itself — they load from the server's catalog.
/// </summary>
public sealed class ClientVerticalBoundaryTests
{
    private const string NeutralOperatorUi = "GeoAssets.NeutralOperator.UI";
    private const string IpBillingUi = "GeoAssets.IpBilling.UI";

    private static readonly string[] SkippedDirectories = ["bin", "obj"];

    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln was not found above the test output directory.");
    }

    private static string Read(params string[] relativePath) => File.ReadAllText(Path.Combine([Root, .. relativePath]));

    private static IReadOnlyList<string> ProjectReferences(params string[] csproj) =>
        [.. XDocument.Parse(Read(csproj)).Descendants("ProjectReference").Select(r => (string)r.Attribute("Include")!)];

    private static IEnumerable<string> SourcesUnder(params string[] directory)
    {
        var root = Path.Combine([Root, .. directory]);
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".razor", StringComparison.Ordinal))
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(segment => SkippedDirectories.Contains(segment, StringComparer.Ordinal)));
    }

    /// <summary>The text of the <c>AdditionalAssemblies</c> attribute of the router in <paramref name="razor"/>.</summary>
    private static string AdditionalAssemblies(string razor)
    {
        var start = razor.IndexOf("AdditionalAssemblies", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the router lists the assemblies its routes live in");
        return razor[start..razor.IndexOf('>', start)];
    }

    [Fact]
    public void Shared_DoesNotReferenceIpBillingOrNeutralOperator()
    {
        ProjectReferences("apps", "GeoAssets.Shared", "GeoAssets.Shared.csproj")
            .Should().NotContain(r => r.Contains("IpBilling", StringComparison.Ordinal) || r.Contains("NeutralOperator", StringComparison.Ordinal));

        var sources = SourcesUnder("apps", "GeoAssets.Shared").ToList();
        sources.Should().NotBeEmpty("the scan must actually find the Shared sources");
        var offenders = sources
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("GeoAssets.IpBilling", StringComparison.Ordinal) || text.Contains("GeoAssets.NeutralOperator", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(Root, path))
            .Order(StringComparer.Ordinal);

        offenders.Should().BeEmpty("a vertical's types belong to its own UI library, not the shell");
    }

    [Fact]
    public void Clients_DoNotRegisterSharedUseOrderTypes()
    {
        Read("apps", "GeoAssets.Web", "Program.cs").Should().NotContain("RegisterAll");
        Read("apps", "GeoAssets.MAUI", "MauiProgram.cs").Should().NotContain("RegisterAll");
    }

    [Theory]
    [InlineData("GeoAssets.Web", "GeoAssets.Web.csproj")]
    [InlineData("GeoAssets.MAUI", "GeoAssets.MAUI.csproj")]
    public void Clients_ReferenceBothVerticalUiLibraries(string app, string csproj)
    {
        var references = ProjectReferences("apps", app, csproj);

        references.Should().Contain(r => r.Contains(NeutralOperatorUi + ".csproj", StringComparison.Ordinal));
        references.Should().Contain(r => r.Contains(IpBillingUi + ".csproj", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("GeoAssets.Web", "App.razor")]
    [InlineData("GeoAssets.MAUI", "WebApp.razor")]
    public void Clients_RouterListsBothVerticalAssemblies(string app, string razor)
    {
        var assemblies = AdditionalAssemblies(Read("apps", app, razor));

        assemblies.Should().Contain(NeutralOperatorUi).And.Contain(IpBillingUi);
    }

    [Theory]
    [InlineData("GeoAssets.Web", "Program.cs")]
    [InlineData("GeoAssets.MAUI", "MauiProgram.cs")]
    public void Clients_ComposeTheVerticalsIntoTheShell(string app, string program)
    {
        var text = Read("apps", app, program);

        text.Should().Contain("AddOrderDetailPanels()").And.Contain("AddNeutralOperatorUi()");
        text.Should().Contain("AddGeoAssetsNavigation(");
        text.Should().Contain("typeof(IspTenantPortalMenuItem).Assembly").And.Contain("typeof(AdminIpBillingMunicipiosMenuItem).Assembly");
    }

    [Fact]
    public void TheMauiHost_LoadsTheOrderTypesFromTheServer()
    {
        // The MAUI project is not buildable from the test projects (net10.0-only targets), so the wiring is pinned as text.
        File.Exists(Path.Combine(Root, "apps", "GeoAssets.MAUI", "Services", "OrderTypeCatalogLoader.cs")).Should().BeTrue();
        Read("apps", "GeoAssets.MAUI", "MauiProgram.cs").Should().Contain("OrderTypeCatalogLoader");
    }
}
