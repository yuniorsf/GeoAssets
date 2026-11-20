namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// Operations tool (XD01-391): moves the NeutralOperator orders the kernel hosted to NeutralOperator.Server's database.
/// See <see cref="DataMoveOptions.Usage"/> and <c>deploy/neutral-operator/README.md</c>.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = CancelOnCtrlC();
        var application = new DataMoveApplication(DataMoveRunner.RunAsync, Console.Out, Console.Error);
        return await application.RunAsync(
            args,
            Environment.GetEnvironmentVariable(DataMoveOptions.SourceVariable),
            Environment.GetEnvironmentVariable(DataMoveOptions.TargetVariable),
            cancellation.Token).ConfigureAwait(false);
    }

    /// <summary>A source Ctrl+C cancels instead of killing the process, so open transactions roll back.</summary>
    private static CancellationTokenSource CancelOnCtrlC()
    {
        var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        return cancellation;
    }
}
