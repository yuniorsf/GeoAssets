using FluentAssertions;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Workflow.Hosting;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Rest;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Architecture;

/// <summary>
/// The shape ADR-001 gives an extracted vertical (XD01-382, AC5): the NeutralOperator host reaches the kernel only over REST — it
/// never references <c>GeoAssets.Server</c> or the kernel's databases — and the order-type chassis it runs on, <c>GeoAssets.Workflow.Hosting</c>,
/// carries no vertical.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class NeutralOperatorServerArchitectureTests
{
    private static readonly string[] VerticalAssemblyPrefixes =
    [
        "GeoAssets.NeutralOperator", "GeoAssets.FieldService", "GeoAssets.IpBilling", "GeoAssets.Domain", "GeoAssets.Integrations",
    ];

    private static readonly System.Reflection.Assembly Host = typeof(HttpTenantContext).Assembly;

    [Fact]
    public void NeutralOperatorServer_DoesNotReference_TheKernelHost_DirectlyOrThroughAnotherAssembly()
    {
        Host.GetReferencedAssemblies().Select(a => a.Name).Should().NotContain("GeoAssets.Server");
        AssemblyClosure.Of(Host).Should().NotContain("GeoAssets.Server", "the kernel is reached over REST only; nothing of it is linked in");
    }

    [Fact]
    public void NeutralOperatorServer_DoesNotReference_TheKernelsDatabases_OrAnotherVerticalHost()
    {
        var closure = AssemblyClosure.Of(Host);

        closure.Should().NotContain("GeoAssets.Provider.PostgreSQL", "features are the kernel's, read through its REST API");
        closure.Should().NotContain("GeoAssets.Identity.EFCore", "identity is the kernel's: the caller's context is read from its /api/identity/me");
        closure.Should().NotContain("GeoAssets.FieldService.Server").And.NotContain("GeoAssets.IpBilling.Server").And.NotContain("GeoAssets.Gateway");
        closure.Should().NotContain("GeoAssets.FieldService.Persistence", "another vertical's database context is not this host's");
        closure.Should().NotContain("GeoAssets.IpBilling.EFCore");
    }

    [Fact]
    public void NeutralOperatorServer_Links_TheNeutralOperatorModuleLibrary_AndTheChassis()
    {
        var closure = AssemblyClosure.Of(Host);

        closure.Should().Contain(typeof(NeutralOperatorModule).Assembly.GetName().Name!);
        closure.Should().Contain(typeof(ServerWorkflowPrincipalFactory).Assembly.GetName().Name!);
        closure.Should().Contain(typeof(RestTargetLedgerClient).Assembly.GetName().Name!);
    }

    [Fact]
    public void TheChassis_WorkflowHosting_ReferencesNoVerticalDirectly_AndNoHost()
    {
        var chassis = typeof(ServerWorkflowPrincipalFactory).Assembly;

        chassis.GetReferencedAssemblies().Select(a => a.Name!).Where(name => name.StartsWith("GeoAssets.", StringComparison.Ordinal))
            .Should().NotContain(name => VerticalAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)),
                "the order-type chassis serves any vertical and names none");
        AssemblyClosure.Of(chassis).Should().NotContain("GeoAssets.Server").And.NotContain("GeoAssets.Provider.PostgreSQL")
            .And.NotContain("GeoAssets.NeutralOperator.Modules", "the module library depends on the chassis, never the reverse");
        chassis.GetTypes().Select(t => t.Namespace).OfType<string>().Should().OnlyContain(
            ns => ns.StartsWith("GeoAssets.Workflow.Hosting", StringComparison.Ordinal), "its types are the chassis' own");
    }

    [Fact]
    public void TheKernelsRestClients_WorkflowRest_ReferenceNoHost_AndNoVerticalPersistence()
    {
        var closure = AssemblyClosure.Of(typeof(RestTargetLedgerClient).Assembly);

        closure.Should().NotContain("GeoAssets.Server").And.NotContain("GeoAssets.NeutralOperator.Server");
        closure.Should().NotContain("GeoAssets.NeutralOperator.Persistence").And.NotContain("GeoAssets.NeutralOperator.Modules");
    }

    [Fact]
    public void TheHostsServiceOrderModel_MapsNoVerticalTable()
    {
        var connectionString = "Host=localhost;Database=never_opened";
        using var db = new ServiceOrderDbContext(new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql(connectionString).Options);

        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).Where(t => t is not null).Select(t => t!).ToList();

        tables.Should().Contain(["ServiceOrders", "ServiceOrderTargets", "OrderDispatches", "OrderActionLogs", "OrderTypes", "WorkflowOutboxMessages"]);
        tables.Should().NotContain(t => t.StartsWith("field_", StringComparison.Ordinal) || t.Contains("Occupanc", StringComparison.Ordinal)
            || t.Contains("Contractor", StringComparison.Ordinal) || t.StartsWith("UsCatalog", StringComparison.Ordinal),
            "the vertical's tables are its own contexts', never the engine's");
    }
}
