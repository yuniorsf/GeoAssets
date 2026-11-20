namespace GeoAssets.Tools.GatewayLoadProfile;

/// <summary>
/// Developer tool (XD01-393): the request-count model behind the gateway's rate-limit defaults, and a replay of that
/// profile against a gateway reporting requests/second per class. See <see cref="LoadProfileOptions.Usage"/> and
/// <c>docs/perf/gateway-rate-limits.md</c>.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var (options, error) = LoadProfileOptions.Parse(args, Environment.GetEnvironmentVariable(LoadProfileOptions.TokenVariable));
        if (options is null)
        {
            await Console.Error.WriteLineAsync(error);
            await Console.Error.WriteLineAsync(LoadProfileOptions.Usage);
            return 2;
        }

        await Console.Out.WriteLineAsync(options.Model.Describe());
        if (string.Equals(options.Command, "model", StringComparison.Ordinal)) return 0;

        using var cancellation = new CancellationTokenSource();
        CancelOnCtrlC(cancellation);

        using var http = new HttpClient { BaseAddress = options.BaseUrl, Timeout = TimeSpan.FromSeconds(30) };
        var runner = new LoadProfileRunner(http, options.Model, options, TimeProvider.System, Console.Out);
        await runner.RunAsync(cancellation.Token);
        return 0;
    }

    /// <summary>Ctrl+C stops the replay (and it still reports what was sent) instead of killing the process.</summary>
    private static void CancelOnCtrlC(CancellationTokenSource cancellation) =>
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
}
