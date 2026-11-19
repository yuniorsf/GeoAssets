using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.NeutralOperator.Application;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>XD01-375: the NeutralOperator vertical lives in its own assemblies; the engine and Core carry none of it.</summary>
[Trait("Category", "Architecture")]
public sealed class VerticalNamespaceTests
{
    private static readonly string[] VerticalSegments = ["Isp", "NeutralOperator", "Occupancy", "SharedUse"];

    private static Assembly CoreAssembly => typeof(GeoFeature).Assembly;

    private static Assembly EngineAssembly => typeof(OrderTypeRegistry).Assembly;

    private static Assembly DomainAssembly => typeof(PoleAttachmentSlotMatrix).Assembly;

    private static Assembly ApplicationAssembly => typeof(NeutralOperatorServiceExtensions).Assembly;

    private static IEnumerable<string> GeoAssetsReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("GeoAssets.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    [Fact]
    public void EngineAndCore_ContainNoVerticalNamespaces()
    {
        var offenders = CoreAssembly.GetTypes().Concat(EngineAssembly.GetTypes())
            .Where(type => type.Namespace is not null
                && type.Namespace.Split('.').Intersect(VerticalSegments, StringComparer.Ordinal).Any())
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty("the vertical namespaces (Isp, NeutralOperator, Occupancy, SharedUse) live in the NeutralOperator assemblies (XD01-375); offenders: {0}", string.Join(", ", offenders));
    }

    [Fact]
    public void NeutralOperatorDomain_DependsOnCoreOnly()
    {
        GeoAssetsReferences(DomainAssembly).Should().BeEquivalentTo(["GeoAssets.Core"]);
    }

    [Fact]
    public void NeutralOperatorApplication_DependsOnDomainEngineAndCore()
    {
        GeoAssetsReferences(ApplicationAssembly).Should().BeSubsetOf(
            ["GeoAssets.NeutralOperator.Domain", "GeoAssets.Workflow", "GeoAssets.Core", "GeoAssets.FieldService.Application"]);
    }
}
