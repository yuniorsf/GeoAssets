// Console app, not an xUnit test harness: the EXPLAIN-ANALYZE-on-a-persistent-200k-row-db
// workflow and repeated migrate/seed/bench invocations across phases don't fit
// PostgresContainerFixture's model (an ephemeral Testcontainers instance spun up/down per test
// run) — this needs one stable, inspectable geoassets_bench database (XD01-171).
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GeoAssets.Bench.GetPageAsync;

internal static class Program
{
    private const string ConnectionString =
        "Host=localhost;Port=5434;Database=geoassets_bench;Username=postgres;Password=postgres";

    private static DbContextOptions<GeoAssetsDbContext> CreateOptions(CapturingInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseNetTopologySuite());
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return builder.Options;
    }

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: migrate <target>|seed|bench --label <before|after> --out <path>");
            return 1;
        }

        switch (args[0])
        {
            case "migrate":
                await RunMigrateAsync(args[1]);
                return 0;
            case "seed":
                await RunSeedAsync();
                return 0;
            case "bench":
                var label = GetArg(args, "--label") ?? throw new ArgumentException("--label required");
                var outPath = GetArg(args, "--out") ?? throw new ArgumentException("--out required");
                await RunBenchAsync(label, outPath);
                return 0;
            default:
                Console.Error.WriteLine($"Unknown mode '{args[0]}'");
                return 1;
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // ── migrate ──────────────────────────────────────────────────────────────

    private static async Task RunMigrateAsync(string target)
    {
        await using var db = new GeoAssetsDbContext(CreateOptions());
        var migrator = db.Database.GetService<IMigrator>();
        string? targetMigration = target switch
        {
            "latest" => null,
            "0" => "0",
            _ => target
        };
        Console.WriteLine($"Migrating to '{target}' (targetMigration={targetMigration ?? "<latest>"})...");
        await migrator.MigrateAsync(targetMigration);
        Console.WriteLine("Done.");
    }

    // ── seed ─────────────────────────────────────────────────────────────────

    private static async Task RunSeedAsync()
    {
        await using var db = new GeoAssetsDbContext(CreateOptions());
        Console.WriteLine("Truncating geo_entity...");
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity");

        Console.WriteLine("Seeding 200,000 rows...");
        var sw = Stopwatch.StartNew();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "Description", "CreatedAt", "UpdatedAt", geom, custom_attributes, topology)
            SELECT
                'bench-' || lpad(gs::text, 6, '0'),
                'P-' || lpad(gs::text, 6, '0'),
                CASE (gs / 17) % 3
                    WHEN 0 THEN '00000000-0000-0000-0000-000000000004'
                    WHEN 1 THEN '00000000-0000-0000-0000-000000000005'
                    ELSE '00000000-0000-0000-0000-000000000006'
                END,
                'Utility pole, sector ' || (gs % 50) || ', feeder ' || (gs % 12),
                TIMESTAMP '2026-01-01 00:00:00' + (gs || ' seconds')::interval,
                TIMESTAMP '2026-01-01 00:00:00' + (gs || ' seconds')::interval,
                ST_SetSRID(ST_MakePoint(-58.37 + (gs % 1000) * 0.0001, -34.60 + (gs % 1000) * 0.0001), 4326),
                jsonb_build_object(
                    'material', CASE gs % 3 WHEN 0 THEN 'concreto' WHEN 1 THEN 'madera' ELSE 'metal' END,
                    'fases', ((gs % 3) + 1)::text,
                    'altura_m', (8 + gs % 5)::text,
                    'circuito', 'CIRC-' || lpad((gs % 500)::text, 4, '0'),
                    'codigo_lote', 'LOTE-' || lpad((gs % 200)::text, 4, '0'),
                    'serie', 'SN-' || lpad(gs::text, 6, '0')
                ),
                '[]'::jsonb
            FROM generate_series(1, 200000) AS gs
            """);
        Console.WriteLine($"Seeded in {sw.Elapsed.TotalSeconds:F1}s.");
    }

    // ── bench ────────────────────────────────────────────────────────────────

    private static readonly (string Case, string? SearchText)[] SearchCases =
    [
        ("rare name", "P-100000"),
        ("rare attribute value", "SN-100000"),
        ("attribute key match", "altura_m"),
        ("no match", "zzz"),
        ("common term", "concreto"),
    ];

    private static readonly string[] SortFields = ["name", "createdAt", "updatedAt"];
    private static readonly (string Label, int Skip)[] Pages = [("page 1", 0), ("page 200", 9950)];
    private static readonly (string Label, string? AssetTypeId)[] TypeFilters =
    [
        ("none", null),
        ("Poste", "00000000-0000-0000-0000-000000000004"),
    ];

    private static async Task RunBenchAsync(string label, string outPath)
    {
        var results = new List<CombinationResult>();

        foreach (var (caseName, searchText) in SearchCases)
        foreach (var sortBy in SortFields)
        foreach (var (pageLabel, skip) in Pages)
        foreach (var (filterLabel, assetTypeId) in TypeFilters)
        {
            var query = new AssetQuery
            {
                SearchText = searchText,
                SortBy = sortBy,
                Skip = skip,
                Take = 50,
                AssetTypeId = assetTypeId,
            };

            Console.WriteLine($"[{label}] {caseName} | sort={sortBy} | {pageLabel} | type={filterLabel}");

            var interceptor = new CapturingInterceptor();
            await using (var warmupProvider = new PostgresAssetProvider(
                new GeoAssetsDbContext(CreateOptions(interceptor)), CreateOptions(interceptor),
                NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System))
            {
                await warmupProvider.GetPageAsync(query);
            }
            var captured = interceptor.Captured.ToList();

            var timingsMs = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                var options = CreateOptions();
                await using var provider = new PostgresAssetProvider(
                    new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
                var sw = Stopwatch.StartNew();
                await provider.GetPageAsync(query);
                sw.Stop();
                timingsMs.Add(sw.Elapsed.TotalMilliseconds);
            }
            timingsMs.Sort();
            var medianMs = timingsMs[2];

            var statements = new List<StatementResult>();
            foreach (var cmd in captured)
            {
                var plan = await ExplainAsync(cmd);
                statements.Add(plan);
            }
            var totalExecMs = statements.Sum(s => s.ExecutionTimeMs ?? 0);
            foreach (var s in statements)
                s.PercentOfTotal = totalExecMs > 0 ? 100.0 * (s.ExecutionTimeMs ?? 0) / totalExecMs : 0;

            results.Add(new CombinationResult
            {
                SearchCase = caseName,
                SearchText = searchText,
                SortBy = sortBy,
                Page = pageLabel,
                TypeFilter = filterLabel,
                MedianMs = medianMs,
                Statements = statements,
            });
        }

        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outPath, json);
        Console.WriteLine($"Wrote {results.Count} combinations to {outPath}");
    }

    private static async Task<StatementResult> ExplainAsync(CapturedCommand cmd)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var explainCmd = connection.CreateCommand();
        explainCmd.CommandText = "EXPLAIN (ANALYZE, BUFFERS) " + cmd.CommandText;
        foreach (var p in cmd.Parameters)
        {
            explainCmd.Parameters.Add(new NpgsqlParameter
            {
                ParameterName = p.ParameterName,
                Value = p.Value,
                NpgsqlDbType = p.NpgsqlDbType,
            });
        }

        var lines = new List<string>();
        await using (var reader = await explainCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                lines.Add(reader.GetString(0));
        }
        var plan = string.Join('\n', lines);

        var match = Regex.Match(plan, @"Execution Time:\s*([\d.]+)\s*ms");
        double? execMs = match.Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;

        return new StatementResult
        {
            Sql = cmd.CommandText,
            Params = cmd.Parameters.Select(DescribeParam).ToList(),
            Plan = plan,
            SeqScanOnGeoEntity = plan.Contains("Seq Scan on geo_entity", StringComparison.Ordinal),
            ExecutionTimeMs = execMs,
        };
    }

    private static string DescribeParam(NpgsqlParameter p)
    {
        if (p.Value is System.Collections.IEnumerable enumerable && p.Value is not string)
        {
            var items = enumerable.Cast<object?>().Take(10).Select(v => v?.ToString() ?? "null").ToList();
            var total = enumerable.Cast<object?>().Count();
            var suffix = total > 10 ? $", ... ({total} total)" : "";
            return $"{p.ParameterName}=[{string.Join(", ", items)}{suffix}]";
        }
        return $"{p.ParameterName}={p.Value}";
    }
}

internal sealed class CapturedCommand
{
    public required string CommandText { get; init; }
    public required List<NpgsqlParameter> Parameters { get; init; }
}

internal sealed class CapturingInterceptor : DbCommandInterceptor
{
    public List<CapturedCommand> Captured { get; } = [];

    private void Capture(System.Data.Common.DbCommand command)
    {
        var clonedParams = command.Parameters
            .Cast<NpgsqlParameter>()
            .Select(p => new NpgsqlParameter { ParameterName = p.ParameterName, Value = p.Value, NpgsqlDbType = p.NpgsqlDbType })
            .ToList();
        Captured.Add(new CapturedCommand { CommandText = command.CommandText, Parameters = clonedParams });
    }

    public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Capture(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Capture(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}

internal sealed class CombinationResult
{
    public required string SearchCase { get; init; }
    public required string? SearchText { get; init; }
    public required string SortBy { get; init; }
    public required string Page { get; init; }
    public required string TypeFilter { get; init; }
    public required double MedianMs { get; init; }
    public required List<StatementResult> Statements { get; init; }
}

internal sealed class StatementResult
{
    public required string Sql { get; init; }
    public required List<string> Params { get; init; }
    public required string Plan { get; init; }
    public required bool SeqScanOnGeoEntity { get; init; }
    public required double? ExecutionTimeMs { get; init; }
    public double PercentOfTotal { get; set; }
}
