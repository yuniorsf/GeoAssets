using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

/// <summary>Asks the kernel's real target-ledger store what holds a feature open — the read every new reservation's conflict check starts from.</summary>
internal static class LedgerProbe
{
    public static async Task<IReadOnlyList<TargetLedgerUsage>> OpenUsagesAsync(string kernelConnectionString, params string[] featureIds)
    {
        var registry = new OrderTypeRegistry();
        NeutralOperatorModule.RegisterOrderTypes(registry);

        var db = MoveDatabases.ServiceOrderContext(kernelConnectionString, MoveDatabases.KernelMigrationsAssembly);
        await using (db.ConfigureAwait(false))
        {
            var store = new EFTargetLedgerStore(db, registry);
            var usages = await store.FindOpenUsagesAsync(featureIds, new TargetLedgerKey(TargetLedger.KernelVerticalId, "another-order"), CancellationToken.None).ConfigureAwait(false);
            return [.. usages.OrderBy(u => u.Usage.FeatureId, StringComparer.Ordinal)];
        }
    }
}
