using FluentAssertions;
using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// XD01-405: the plan PostgreSQL picks for <see cref="EFMunicipioGrantSyncOutbox.PruneAsync"/>'s id-select (XD01-400) at
/// production volume — 5,570 municípios (every one in Brazil), a year-long backlog of completed requests, 30 days of
/// recent ones, and a few failed and pending ones. The plan is the one of the statement EF really sends, with its bound
/// parameters (<c>auto_explain</c> on the context's own connection gives its <c>EXPLAIN (ANALYZE, BUFFERS)</c> output),
/// after <c>VACUUM ANALYZE</c>, so the planner works from production-like statistics. Only the plan shape is asserted, never a time; the plans and timings are recorded in
/// <c>docs/perf/XD01-405-grant-sync-prune-plan.md</c>.
/// </summary>
public sealed class EFMunicipioGrantSyncOutboxPrunePlanTests(ITestOutputHelper output)
{
    /// <summary>The <c>GrantSync:PruneBatchSize</c> default.</summary>
    private const int BatchSize = 500;

    private const int Municipios = 5_570;
    private const int SeededRows = 70_210;

    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The <c>GrantSync:CompletedRetention</c> default (30 days).</summary>
    private static readonly DateTimeOffset CompletedBefore = Now - TimeSpan.FromDays(30);

    /// <summary>A <c>GrantSync:FailedRetention</c> of 90 days.</summary>
    private static readonly DateTimeOffset FailedBefore = Now - TimeSpan.FromDays(90);

    /// <summary>
    /// Inserted in time order, so the ids grow with <c>RequestedAt</c> as they do in production. Per município: ten completed
    /// requests between 370 and 100 days ago (the backlog); then, for the odd ones, one completed 60 days ago (their latest,
    /// kept) and, for the even ones, four completed in the last 30 days. Every 25th município also has a request that failed
    /// 120 days ago, the 12th of every 25 one that failed 45 days ago, and the 7th of every 40 one pending since the last hour.
    /// </summary>
    private const string SeedSql =
        """
        WITH c AS (SELECT timestamptz '2026-11-20 12:00:00+00' AS now),
        r AS (
            SELECT m, c.now - (400 - 30 * b) * interval '1 day' + m * interval '1 second' AS at, 'completed' AS state
              FROM c, generate_series(1, 5570) AS m, generate_series(1, 10) AS b
            UNION ALL
            SELECT m, c.now - interval '60 days' + m * interval '1 second', 'completed'
              FROM c, generate_series(1, 5570) AS m WHERE m % 2 = 1
            UNION ALL
            SELECT m, c.now - (7 * w - 3) * interval '1 day' + m * interval '1 second', 'completed'
              FROM c, generate_series(1, 5570) AS m, generate_series(1, 4) AS w WHERE m % 2 = 0
            UNION ALL
            SELECT m, c.now - interval '120 days' + m * interval '1 second', 'failed'
              FROM c, generate_series(1, 5570) AS m WHERE m % 25 = 0
            UNION ALL
            SELECT m, c.now - interval '45 days' + m * interval '1 second', 'failed'
              FROM c, generate_series(1, 5570) AS m WHERE m % 25 = 12
            UNION ALL
            SELECT m, c.now - interval '1 hour' + m * interval '0.5 second', 'pending'
              FROM c, generate_series(1, 5570) AS m WHERE m % 40 = 7
        )
        INSERT INTO "MunicipioGrantSyncRequests"
            ("MunicipioId", "Kind", "IbgeCode", "DistribuidoraOrganizationId", "PrefeituraOrganizationId",
             "RequestedBy", "RequestedAt", "Attempts", "NextAttemptAt", "LastError", "CompletedAt", "FailedAt")
        SELECT ('00000000-0000-4000-8000-' || lpad(to_hex(r.m), 12, '0'))::uuid,
               'Sync',
               lpad(r.m::text, 7, '0'),
               'd0d0d0d0-0000-4000-8000-000000000405',
               ('a0a0a0a0-0000-4000-8000-' || lpad(to_hex(r.m), 12, '0'))::uuid,
               'seed',
               r.at,
               CASE r.state WHEN 'failed' THEN 20 ELSE 0 END,
               r.at,
               CASE r.state WHEN 'failed' THEN 'HttpRequestException: kernel unavailable' END,
               CASE r.state WHEN 'completed' THEN r.at + interval '1 minute' END,
               CASE r.state WHEN 'failed' THEN r.at + interval '1 hour' END
          FROM r
         ORDER BY r.at, r.m
        """;

    [Fact]
    public async Task PruneSelect_FullBacklog_WalksAnIndex_AndStopsAtTheBatch()
    {
        var database = await SeedAsync();

        var (matched, plan) = await ExplainPruneSelectAsync(database, CompletedBefore, null, "full backlog");

        matched.Should().Be(BatchSize);
        AssertNoSequentialScan(plan);
    }

    [Fact]
    public async Task PruneSelect_FullBacklog_WithFailedRetention_WalksAnIndex_AndStopsAtTheBatch()
    {
        var database = await SeedAsync();

        var (matched, plan) = await ExplainPruneSelectAsync(database, CompletedBefore, FailedBefore, "full backlog, FailedRetention set");

        matched.Should().Be(BatchSize);
        AssertNoSequentialScan(plan);
    }

    [Fact]
    public async Task PruneSelect_SteadyState_NothingDue_NeverScansTheTable()
    {
        var database = await SeedAsync();
        await PruneToCompletionAsync(database, CompletedBefore, null);

        var (matched, plan) = await ExplainPruneSelectAsync(database, CompletedBefore, null, "steady state, nothing due");

        matched.Should().Be(0);
        AssertNoSequentialScan(plan);
    }

    [Fact]
    public async Task PruneSelect_SteadyState_WithFailedRetention_NeverScansTheTable()
    {
        var database = await SeedAsync();
        await PruneToCompletionAsync(database, CompletedBefore, null);

        var (matched, plan) = await ExplainPruneSelectAsync(database, CompletedBefore, FailedBefore, "steady state, FailedRetention set");

        matched.Should().Be(Municipios / 25, "only the superseded requests that failed 120 days ago are due");
        AssertNoSequentialScan(plan);
    }

    /// <summary>
    /// The shape XD01-400 relied on: the requests are walked in <c>Id</c> order on the primary key, and the "a later request
    /// of the same município exists" check probes the <c>(MunicipioId, Id)</c> index — never a sequential scan of the table.
    /// </summary>
    private static void AssertNoSequentialScan(string plan)
    {
        plan.Should().NotContain("Seq Scan", "the prune must not read the whole outbox per batch");
        plan.Should().Contain("""Index Scan using "PK_MunicipioGrantSyncRequests" on "MunicipioGrantSyncRequests" m """);
        plan.Should().Contain("""using "IX_MunicipioGrantSyncRequests_MunicipioId_Id" on "MunicipioGrantSyncRequests" m0 """);
    }

    private static async Task<string> SeedAsync()
    {
        var database = IpBillingPostgres.NewDatabaseName();
        await using var db = await IpBillingPostgres.NewContextAsync(database);
        await db.Database.MigrateAsync();
        (await db.Database.ExecuteSqlRawAsync(SeedSql)).Should().Be(SeededRows);
        await AnalyzeAsync(db);
        return database;
    }

    /// <summary>The steady state: what the hourly runs leave once the backlog is gone, vacuumed as autovacuum would.</summary>
    private static async Task PruneToCompletionAsync(string database, DateTimeOffset completedBefore, DateTimeOffset? failedBefore)
    {
        await using var db = await IpBillingPostgres.NewContextAsync(database);
        var outbox = new EFMunicipioGrantSyncOutbox(db, new FakeTimeProvider(Now));
        int deleted;
        do
        {
            deleted = await outbox.PruneAsync(completedBefore, failedBefore, BatchSize);
        }
        while (deleted >= BatchSize);

        await AnalyzeAsync(db);
    }

    // VACUUM can't run inside a multi-statement batch or a transaction, hence a command of its own.
    private static Task<int> AnalyzeAsync(IpBillingDbContext db) =>
        db.Database.ExecuteSqlRawAsync("""VACUUM ANALYZE "MunicipioGrantSyncRequests" """);

    /// <summary>
    /// Runs the real <see cref="EFMunicipioGrantSyncOutbox.PruneAsync"/> once, inside a transaction it rolls back (so the data
    /// stays as seeded), with <c>auto_explain</c> loaded on its connection: each statement it runs is planned and executed
    /// exactly as EF sent it, bound parameters included, and its <c>EXPLAIN (ANALYZE, BUFFERS)</c> output comes back as a
    /// notice. Returns how many requests the batch deleted and the id-select's plan.
    /// </summary>
    private async Task<(int Matched, string Plan)> ExplainPruneSelectAsync(
        string database, DateTimeOffset completedBefore, DateTimeOffset? failedBefore, string scenario)
    {
        await using var db = await IpBillingPostgres.NewContextAsync(database);
        var outbox = new EFMunicipioGrantSyncOutbox(db, new FakeTimeProvider(Now));
        var plans = new List<string>();

        await db.Database.OpenConnectionAsync();
        ((NpgsqlConnection)db.Database.GetDbConnection()).Notice += (_, e) => plans.Add(e.Notice.MessageText);
        await db.Database.ExecuteSqlRawAsync("LOAD 'auto_explain'");
        await db.Database.ExecuteSqlRawAsync(
            """
            SET auto_explain.log_min_duration = 0;
            SET auto_explain.log_analyze = on;
            SET auto_explain.log_buffers = on;
            SET auto_explain.log_timing = on;
            SET auto_explain.log_level = notice;
            """);
        plans.Clear();

        int matched;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            matched = await outbox.PruneAsync(completedBefore, failedBefore, BatchSize);
            await tx.RollbackAsync();
        }

        var plan = plans.Should()
            .ContainSingle(p => p.Contains("Query Text: SELECT", StringComparison.Ordinal), "PruneAsync selects once per batch")
            .Subject;
        output.WriteLine($"== {scenario}: {matched} matched");
        foreach (var statement in plans) output.WriteLine(statement);
        return (matched, plan);
    }
}
