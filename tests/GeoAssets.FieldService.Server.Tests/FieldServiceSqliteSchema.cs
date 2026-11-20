using GeoAssets.FieldService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// The FieldService tables (XD01-381) on a SQLite test database whose kernel context was just <c>EnsureCreated</c>:
/// <see cref="FieldServiceDbContext"/> shares the kernel context's connection, but the kernel's create step no longer
/// builds the vertical's tables, so the vertical's own create script is run (SQLite ignores the schema). The postings' and
/// paving measurements' foreign keys to the kernel's <c>ServiceOrders</c> are database-only — the vertical's model does not
/// map the kernel's orders; on PostgreSQL its baseline migration creates them — so they are added to the script, as Restrict,
/// to keep the behaviour the suites ran against before the tables moved.
/// </summary>
internal static class FieldServiceSqliteSchema
{
    public static void Create(FieldServiceDbContext fieldService)
    {
        ArgumentNullException.ThrowIfNull(fieldService);

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
