using System.Reflection;
using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests;

/// <summary>
/// The tool names the heads of the two <c>ServiceOrderDbContext</c> chains it was built against (<see cref="RequiredMigrations"/>) because
/// those chains live in host assemblies a tool does not reference. These tests do reference them, so a migration added to either chain
/// without the tool being reviewed (and its constant updated) fails here rather than in an operator's preflight.
/// </summary>
public sealed class MigrationHeadGuardTests
{
    private static string HeadOf(string assemblyName)
    {
        var migrations = Assembly.Load(assemblyName).GetTypes()
            .Where(t => t.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(ServiceOrderDbContext))
            .Select(t => t.GetCustomAttribute<MigrationAttribute>()?.Id)
            .OfType<string>()
            .ToList();

        migrations.Should().NotBeEmpty($"{assemblyName} carries the ServiceOrderDbContext chain");
        return migrations.Max(StringComparer.Ordinal)!;
    }

    [Fact]
    public void KernelServiceOrdersHead_IsTheLatestServiceOrderDbContextMigrationOfGeoAssetsServer()
    {
        HeadOf("GeoAssets.Server").Should().Be(
            RequiredMigrations.KernelServiceOrders,
            "a migration added to the kernel's ServiceOrderDbContext chain must be reviewed against this tool and its constant updated");
    }

    [Fact]
    public void NeutralOperatorServerHead_IsTheLatestServiceOrderDbContextMigrationOfGeoAssetsNeutralOperatorServer()
    {
        HeadOf("GeoAssets.NeutralOperator.Server").Should().Be(
            RequiredMigrations.NeutralOperatorServerServiceOrders,
            "a migration added to NeutralOperator.Server's ServiceOrderDbContext chain must be reviewed against this tool and its constant updated");
    }

    [Fact]
    public void TheTwoHeads_AreIdsOfMigrationsTheEfChainsReallyApply()
    {
        foreach (var (assembly, head) in new[] { ("GeoAssets.Server", RequiredMigrations.KernelServiceOrders), ("GeoAssets.NeutralOperator.Server", RequiredMigrations.NeutralOperatorServerServiceOrders) })
        {
            var context = ChainContext(assembly);
            using (context)
                Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetMigrations(context.Database).Should().EndWith(head, $"{head} is the last migration {assembly} applies to ServiceOrderDbContext");
        }
    }

    private static ServiceOrderDbContext ChainContext(string assembly) =>
        Support.MoveDatabases.ServiceOrderContext("Host=unused;Database=unused", assembly);

    [Fact]
    public void ServiceOrderHistoryTable_IsEfsDefault()
    {
        RequiredMigrations.ServiceOrderHistoryTable.Should().Be(HistoryRepository.DefaultTableName);
    }
}
