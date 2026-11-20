using GeoAssets.FieldService.Persistence;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// The FieldService stores' <see cref="FieldServiceDbContext"/> (XD01-381) over a kernel test context's connection, scoped to
/// that context's organization and joining its transaction — the arrangement the FieldService host registers.
/// </summary>
internal static class FieldServiceTestContext
{
    public static FieldServiceDbContext Over(ServiceOrderDbContext kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        var connection = kernel.Database.GetDbConnection();
        var options = new DbContextOptionsBuilder<FieldServiceDbContext>();
        if (kernel.Database.IsSqlite())
            options.UseSqlite(connection);
        else
            options.UseNpgsql(connection, FieldServiceDbContext.ConfigureMigrations);

        return new FieldServiceDbContext(
            options.Options, new FakeTenantContext(kernel.CurrentOrganizationId), new ServiceOrderAmbientTransaction(kernel));
    }

    /// <summary>
    /// Creates the FieldService tables on the SQLite connection of <paramref name="kernel"/>, whose own
    /// <c>EnsureCreated</c> no longer creates them. The create script of the vertical's context is run (SQLite ignores the
    /// schema). The postings' and paving measurements' foreign keys to the kernel's <c>ServiceOrders</c> are database-only —
    /// the vertical's model does not map the kernel's orders; on PostgreSQL its baseline migration creates them — so they are
    /// added to the script here, as Restrict, to keep what the tests rely on: a row cannot reference an order that is not there.
    /// </summary>
    public static void CreateSqliteTables(ServiceOrderDbContext kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        using var fieldService = Over(kernel);
        var script = fieldService.Database.GenerateCreateScript();
        script = WithForeignKeys(script, "field_contractor_us_postings",
            "CONSTRAINT \"FK_field_contractor_us_postings_ServiceOrders_ServiceOrderId\" FOREIGN KEY (\"ServiceOrderId\") REFERENCES \"ServiceOrders\" (\"Id\") ON DELETE RESTRICT");
        script = WithForeignKeys(script, "field_icerp_paving_measurements",
            "CONSTRAINT \"FK_field_icerp_paving_measurements_ServiceOrders_Child\" FOREIGN KEY (\"ChildServiceOrderId\") REFERENCES \"ServiceOrders\" (\"Id\") ON DELETE RESTRICT",
            "CONSTRAINT \"FK_field_icerp_paving_measurements_ServiceOrders_Parent\" FOREIGN KEY (\"ParentServiceOrderId\") REFERENCES \"ServiceOrders\" (\"Id\") ON DELETE RESTRICT");
        fieldService.Database.ExecuteSqlRaw(script);
    }

    private static string WithForeignKeys(string script, string table, params string[] constraints)
    {
        var tableStart = script.IndexOf($"CREATE TABLE \"{table}\"", StringComparison.Ordinal);
        var tableEnd = tableStart < 0 ? -1 : script.IndexOf(");", tableStart, StringComparison.Ordinal);
        if (tableEnd < 0)
            throw new InvalidOperationException($"The FieldService create script no longer declares {table} as expected.");

        while (char.IsWhiteSpace(script[tableEnd - 1]))
            tableEnd--;

        return script.Insert(tableEnd, string.Concat(constraints.Select(c => ",\n    " + c)));
    }
}
