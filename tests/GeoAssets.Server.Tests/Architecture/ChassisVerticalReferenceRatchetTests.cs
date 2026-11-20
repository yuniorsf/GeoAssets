using System.Reflection;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>XD01-388: no chassis library references a vertical — as an assembly or a ProjectReference.</summary>
[Trait("Category", "Architecture")]
public sealed class ChassisVerticalReferenceRatchetTests
{
    private const string CleanProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <ProjectReference Include="..\..\core\GeoAssets.Workflow\GeoAssets.Workflow.csproj" />
            <ProjectReference Include="..\GeoAssets.Workflow.Rest\GeoAssets.Workflow.Rest.csproj" />
          </ItemGroup>
        </Project>
        """;

    private static readonly IReadOnlyDictionary<string, string> NoAllowlist = new Dictionary<string, string>(StringComparer.Ordinal);

    public static TheoryData<string> Chassis => [.. ChassisVerticalReferenceRatchet.ChassisAssemblies];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln was not found above the test output.");
    }

    private static string ProjectFileOf(string assemblyName)
    {
        var root = RepositoryRoot();
        var path = new[] { "core", "workflow" }
            .Select(folder => Path.Combine(root, folder, assemblyName, assemblyName + ".csproj"))
            .Single(File.Exists);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Allowlist_IsEmpty_TheChassisReferencesNoVertical()
    {
        ChassisVerticalReferenceRatchet.Allowlist.Should().BeEmpty("XD01-388 moved the last vertical types out of the chassis — never add an entry back");
    }

    [Theory]
    [MemberData(nameof(Chassis))]
    public void TheChassisAssembly_ReferencesNoVertical_ByAssemblyOrProjectReference(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var referenced = assembly.GetReferencedAssemblies().Select(name => name.Name!);

        var violations = ChassisVerticalReferenceRatchet.Evaluate(
            assemblyName, referenced, ProjectFileOf(assemblyName), ChassisVerticalReferenceRatchet.Allowlist);

        violations.Should().BeEmpty("the chassis names no vertical; a vertical references the chassis, never the reverse");
    }

    [Fact]
    public void TheSet_CoversEveryWorkflowProjectOfTheChassis_OrNamesItOutOfScope()
    {
        var root = RepositoryRoot();
        var projects = new[] { "core", "workflow" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "GeoAssets.Workflow*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToList();

        projects.Should().Contain(ChassisVerticalReferenceRatchet.ChassisAssemblies, "every chassis assembly has a project");
        projects.Except(ChassisVerticalReferenceRatchet.ChassisAssemblies, StringComparer.Ordinal)
            .Should().BeEquivalentTo(ChassisVerticalReferenceRatchet.OutOfScope.Keys, "a new GeoAssets.Workflow* project must join the set or be named out of scope with a reason");
    }

    [Fact]
    public void TheOutOfScopeProjects_AreNotInTheSet_AndEachHasAReason()
    {
        ChassisVerticalReferenceRatchet.OutOfScope.Keys.Should().NotIntersectWith(ChassisVerticalReferenceRatchet.ChassisAssemblies);
        ChassisVerticalReferenceRatchet.OutOfScope.Values.Should().OnlyContain(reason => !string.IsNullOrWhiteSpace(reason));
    }

    // ── The ratchet fails, naming the assembly, when a vertical reference is added back ────────────────────────

    [Theory]
    [InlineData("GeoAssets.NeutralOperator.Rest")]
    [InlineData("GeoAssets.NeutralOperator.Persistence")]
    [InlineData("GeoAssets.FieldService.Application")]
    [InlineData("GeoAssets.IpBilling.Core")]
    [InlineData("GeoAssets.Integrations.Erp")]
    public void Evaluate_AVerticalAssemblyReference_IsReported_NamingTheAssemblyAndTheReference(string vertical)
    {
        var violations = ChassisVerticalReferenceRatchet.Evaluate(
            "GeoAssets.Workflow.Rest", ["System.Runtime", "GeoAssets.Workflow", vertical], CleanProject, NoAllowlist);

        violations.Should().ContainSingle().Which.Should().Contain("GeoAssets.Workflow.Rest").And.Contain(vertical);
    }

    [Theory]
    [InlineData("..\\..\\verticals\\NeutralOperator\\GeoAssets.NeutralOperator.Rest\\GeoAssets.NeutralOperator.Rest.csproj")]
    [InlineData("../../verticals/FieldService/GeoAssets.FieldService.Application/GeoAssets.FieldService.Application.csproj")]
    [InlineData("..\\..\\ipbilling\\GeoAssets.IpBilling.EFCore\\GeoAssets.IpBilling.EFCore.csproj")]
    [InlineData("..\\GeoAssets.NeutralOperator.Thing\\GeoAssets.NeutralOperator.Thing.csproj")]
    public void Evaluate_AVerticalProjectReference_IsReported_NamingTheAssemblyAndTheProject(string include)
    {
        ArgumentNullException.ThrowIfNull(include);
        var project = CleanProject.Replace("</ItemGroup>", $"""<ProjectReference Include="{include}" /></ItemGroup>""", StringComparison.Ordinal);

        var violations = ChassisVerticalReferenceRatchet.Evaluate("GeoAssets.Workflow.EFCore", ["GeoAssets.Workflow"], project, NoAllowlist);

        violations.Should().ContainSingle().Which.Should().Contain("GeoAssets.Workflow.EFCore").And.Contain(include.Replace('\\', '/'));
    }

    [Fact]
    public void Evaluate_ACleanChassisAssembly_HasNoViolation()
    {
        ChassisVerticalReferenceRatchet.Evaluate(
            "GeoAssets.Workflow.Rest", ["System.Runtime", "GeoAssets.Workflow", "GeoAssets.Core"], CleanProject, NoAllowlist)
            .Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_AnAllowlistedReference_IsNotReported_ButItsSiblingIs()
    {
        var allowlist = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GeoAssets.Workflow.Rest -> GeoAssets.NeutralOperator.Rest"] = "synthetic",
        };

        var violations = ChassisVerticalReferenceRatchet.Evaluate(
            "GeoAssets.Workflow.Rest", ["GeoAssets.NeutralOperator.Rest", "GeoAssets.NeutralOperator.Application"], CleanProject, allowlist);

        violations.Should().ContainSingle().Which.Should().Contain("GeoAssets.NeutralOperator.Application");
    }

    [Theory]
    [InlineData("GeoAssets.NeutralOperatorish")]
    [InlineData("GeoAssets.Workflow.NeutralOperator")]
    [InlineData("GeoAssets.FieldServices")]
    public void FindAssemblyViolations_MatchesWholeNamespaceSegmentsOfTheVerticalPrefixes(string name)
    {
        ChassisVerticalReferenceRatchet.FindAssemblyViolations([name]).Should().BeEmpty();
    }

    [Fact]
    public void FindProjectReferenceViolations_ABlankProjectFile_IsRejected()
    {
        var act = () => ChassisVerticalReferenceRatchet.FindProjectReferenceViolations("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheProductionCheck_SeesAChassisProjectFile_WithItsReferences()
    {
        ChassisVerticalReferenceRatchet.FindProjectReferenceViolations(ProjectFileOf("GeoAssets.Workflow.Rest")).Should().BeEmpty();
        ProjectFileOf("GeoAssets.Workflow.Rest").Should().Contain("<ProjectReference", "the scan must be looking at a real project file");
    }

    [Fact]
    public void TheOtherRatchets_StayAtZero()
    {
        KernelReferenceRatchet.Allowlist.Should().BeEmpty();
        VerticalTokenRatchet.Allowlist.Should().BeEmpty();
        VerticalTokenRatchet.DomainReferenceAllowlist.Should().BeEmpty();
    }
}
