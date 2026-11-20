using FluentAssertions;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.NeutralOperator.Persistence.SharedUse;
using GeoAssets.Workflow.EFCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>XD01-388: the shared-use decorators live in the NeutralOperator persistence, not in the engine's EF Core assembly.</summary>
public sealed class WorkflowEFCoreSharedUseMoveTests
{
    [Fact]
    public void TheEngineAssembly_ExposesNoTypeNamedSharedUse()
    {
        var offenders = typeof(WorkflowEFCoreServiceExtensions).Assembly.GetTypes()
            .Where(type => type.Name.Contains("SharedUse", StringComparison.OrdinalIgnoreCase)
                || (type.Namespace?.Contains("SharedUse", StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(type => type.FullName)
            .ToList();

        offenders.Should().BeEmpty("XD01-388 moved the shared-use decorators into GeoAssets.NeutralOperator.Persistence");
    }

    [Fact]
    public void TheEngineExtensions_NoLongerOfferTheSharedUseRegistration()
    {
        typeof(WorkflowEFCoreServiceExtensions).GetMethods().Select(method => method.Name)
            .Should().NotContain("AddSharedUseDispatchPersistence");
    }

    [Fact]
    public void TheDecorators_LiveInTheNeutralOperatorPersistenceAssembly()
    {
        var persistence = typeof(NeutralOperatorPersistenceExtensions).Assembly;

        typeof(ObservableSharedUseDispatchService).Assembly.Should().BeSameAs(persistence);
        typeof(ActivitySharedUseFanOutTracer).Assembly.Should().BeSameAs(persistence);
        typeof(ObservableSharedUseDispatchService).Namespace.Should().Be("GeoAssets.NeutralOperator.Persistence.SharedUse");
        typeof(NeutralOperatorPersistenceExtensions).GetMethods().Select(method => method.Name).Should().Contain("AddSharedUseDispatchPersistence");
    }
}
