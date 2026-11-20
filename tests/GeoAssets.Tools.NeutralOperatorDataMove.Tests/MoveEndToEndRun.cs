using GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests;

/// <summary>
/// Runs the whole move once over a seeded kernel database and keeps what each stage produced, so the end-to-end facts each
/// assert one thing about it without repeating the run. The kernel database is first served by NeutralOperator.Server's own host
/// (the module's routes are the same on both hosts, which is how the kernel served these orders before the cut-over), then moved,
/// and the target database is served by the same host; the two sets of answers are what "identical content" compares.
/// </summary>
internal sealed class MoveEndToEndRun
{
    private static readonly Lazy<Task<MoveEndToEndRun>> _shared = new(CreateAsync);

    private MoveEndToEndRun()
    {
    }

    /// <summary>The one run every fact of the suite reads (it runs on first use, once).</summary>
    public static Task<MoveEndToEndRun> SharedAsync() => _shared.Value;

    public string Kernel { get; private set; } = string.Empty;

    public string Target { get; private set; } = string.Empty;

    public ToolResult Plan { get; private set; } = null!;

    public ToolResult CopyWithoutFlag { get; private set; } = null!;

    public DatabaseState TargetAfterRefusedCopy { get; private set; } = null!;

    public ToolResult Copy { get; private set; } = null!;

    public ToolResult Verify { get; private set; } = null!;

    public ToolResult Remove { get; private set; } = null!;

    public DatabaseState KernelBeforeMove { get; private set; } = null!;

    public DatabaseState KernelAfterCopy { get; private set; } = null!;

    public DatabaseState KernelAfterRemove { get; private set; } = null!;

    public DatabaseState TargetAfterCopy { get; private set; } = null!;

    public DatabaseState TargetAfterRemove { get; private set; } = null!;

    public IReadOnlyList<ServedOrder> ServedByKernel { get; private set; } = [];

    public IReadOnlyList<ServedOrder> ServedByTargetAfterCopy { get; private set; } = [];

    public IReadOnlyList<ServedOrder> ServedByTargetAfterRemove { get; private set; } = [];

    public IReadOnlyList<TargetLedgerUsage> UsagesBeforeRemove { get; private set; } = [];

    public IReadOnlyList<TargetLedgerUsage> UsagesAfterRemove { get; private set; } = [];

    /// <summary>The kernel's ledger rows of the moved orders before <c>remove</c>, as JSON per row id.</summary>
    public IReadOnlyDictionary<string, string> LedgerRowsBeforeRemove { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> LedgerRowsAfterRemove { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public static string[] PolesHeld { get; } = ["pole-a", "pole-b", "pole-c", "pole-d", "pole-e", "pole-z"];

    private static async Task<MoveEndToEndRun> CreateAsync()
    {
        var run = new MoveEndToEndRun();
        await run.ExecuteAsync();
        return run;
    }

    private async Task ExecuteAsync()
    {
        (Kernel, Target) = await MoveTool.SeededPairAsync();
        KernelBeforeMove = await DatabaseState.CaptureAsync(Kernel);

        var kernelHost = NeutralOperatorServerHost.Start(Kernel);
        await using (kernelHost)
            ServedByKernel = await ServedOrder.ReadAsync(kernelHost, KernelSeed.Moved);

        Plan = await MoveTool.RunAsync(Kernel, Target);
        CopyWithoutFlag = await MoveTool.RunAsync(Kernel, Target, "copy");
        TargetAfterRefusedCopy = await DatabaseState.CaptureAsync(Target);
        Copy = await MoveTool.RunAsync(Kernel, Target, "copy", "--include-pending-outbox");
        KernelAfterCopy = await DatabaseState.CaptureAsync(Kernel);
        TargetAfterCopy = await DatabaseState.CaptureAsync(Target);
        Verify = await MoveTool.RunAsync(Kernel, Target, "verify");

        var targetHost = NeutralOperatorServerHost.Start(Target);
        await using (targetHost)
            ServedByTargetAfterCopy = await ServedOrder.ReadAsync(targetHost, KernelSeed.Moved);

        UsagesBeforeRemove = await LedgerProbe.OpenUsagesAsync(Kernel, PolesHeld);
        LedgerRowsBeforeRemove = await LedgerRowsAsync(Kernel);
        Remove = await MoveTool.RunAsync(Kernel, Target, "remove", "--confirm");
        KernelAfterRemove = await DatabaseState.CaptureAsync(Kernel);
        TargetAfterRemove = await DatabaseState.CaptureAsync(Target);
        UsagesAfterRemove = await LedgerProbe.OpenUsagesAsync(Kernel, PolesHeld);
        LedgerRowsAfterRemove = await LedgerRowsAsync(Kernel);

        var hostAfterRemove = NeutralOperatorServerHost.Start(Target);
        await using (hostAfterRemove)
            ServedByTargetAfterRemove = await ServedOrder.ReadAsync(hostAfterRemove, KernelSeed.Moved);
    }

    private static async Task<IReadOnlyDictionary<string, string>> LedgerRowsAsync(string connectionString)
    {
        var rows = await Sql.ColumnAsync(
            connectionString,
            """SELECT "Id" || '=' || to_jsonb(t)::text FROM public."ServiceOrderTargets" t WHERE "ServiceOrderId" IN ('no-created','no-approved','no-staged','no-closed','no-parent','no-child') ORDER BY "Id" """);
        return rows.OfType<string>().ToDictionary(r => r[..r.IndexOf('=', StringComparison.Ordinal)], r => r[(r.IndexOf('=', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);
    }
}
