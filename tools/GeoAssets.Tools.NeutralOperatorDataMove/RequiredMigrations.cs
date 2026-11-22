namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// The heads of the two <c>ServiceOrderDbContext</c> chains this tool was built against. Those chains live in the host
/// assemblies (<c>apps/GeoAssets.Server/Migrations</c>, <c>apps/GeoAssets.NeutralOperator.Server/Migrations</c>), which a
/// tool does not reference, so their heads are named here; a database must have applied its head, and
/// <see cref="SchemaPreflight"/> then checks every moved table's columns against the EF model, so a database ahead of
/// this tool fails on its schema rather than losing a column. The vertical's <c>NeutralOperatorDbContext</c> chain is
/// checked exactly, from its own migrations assembly.
/// </summary>
internal static class RequiredMigrations
{
    /// <summary>The kernel <c>ServiceOrderDbContext</c> chain's head (in <c>GeoAssets.Server</c>).</summary>
    public const string KernelServiceOrders = "20261122224401_AddCrossOrganizationSyncMirrors";

    /// <summary>NeutralOperator.Server's <c>ServiceOrderDbContext</c> chain's head (in <c>GeoAssets.NeutralOperator.Server</c>).</summary>
    public const string NeutralOperatorServerServiceOrders = "20261122224327_AddCrossOrganizationSyncMirrors";

    /// <summary>The history table of both <c>ServiceOrderDbContext</c> chains (EF's default).</summary>
    public const string ServiceOrderHistoryTable = "__EFMigrationsHistory";
}
