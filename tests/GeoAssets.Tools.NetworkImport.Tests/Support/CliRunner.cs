using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Tools.NetworkImport.Tests.Support;

internal static class CliRunner
{
    public static readonly DateTimeOffset Start = new(2026, 11, 26, 10, 20, 30, TimeSpan.Zero);

    public const string Token = "test-token";

    public static IReadOnlyDictionary<string, string?> DefaultEnvironment { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["GEOASSETS_NETWORK_IMPORT_TOKEN"] = Token };

    public static Task<CliResult> RunAsync(params string[] args) =>
        RunAsync(new FakeKernelHandler(), new FakeTimeProvider(Start), args);

    public static Task<CliResult> RunAsync(FakeKernelHandler handler, params string[] args) =>
        RunAsync(handler, new FakeTimeProvider(Start), args);

    public static async Task<CliResult> RunAsync(
        FakeKernelHandler handler,
        FakeTimeProvider clock,
        string[] args,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool advanceClock = true)
    {
        var env = environment ?? DefaultEnvironment;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var dependencies = new NetworkImportDependencies(handler, clock, name => env.GetValueOrDefault(name));

        using var stop = new CancellationTokenSource();
        var pump = advanceClock ? PumpClockAsync(clock, stop.Token) : Task.CompletedTask;
        var exitCode = await NetworkImportApplication.RunAsync(args, stdout, stderr, dependencies, CancellationToken.None);
        await stop.CancelAsync();
        await pump;
        return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task PumpClockAsync(FakeTimeProvider clock, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            try
            {
                await Task.Delay(1, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
