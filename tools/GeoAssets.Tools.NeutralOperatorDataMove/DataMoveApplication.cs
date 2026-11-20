using System.Data.Common;

namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// One invocation: parse the arguments, run the verb through <paramref name="run"/> (so the command line can be exercised
/// without a database), print the report, and answer the exit code. A failed database call or a cancellation is reported
/// by its message only — never with a connection string — and exits <see cref="DataMoveExitCodes.Failed"/>; every open
/// transaction has been rolled back by then.
/// </summary>
internal sealed class DataMoveApplication(
    Func<DataMoveOptions, CancellationToken, Task<DataMoveReport>> run,
    TextWriter output,
    TextWriter error)
{
    private readonly Func<DataMoveOptions, CancellationToken, Task<DataMoveReport>> _run = run ?? throw new ArgumentNullException(nameof(run));
    private readonly TextWriter _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly TextWriter _error = error ?? throw new ArgumentNullException(nameof(error));

    public async Task<int> RunAsync(IReadOnlyList<string> args, string? sourceVariable, string? targetVariable, CancellationToken ct)
    {
        var parsed = DataMoveCommandLine.Parse(args, sourceVariable, targetVariable);
        if (parsed.HelpRequested)
        {
            await _output.WriteLineAsync(DataMoveOptions.Usage.AsMemory(), ct).ConfigureAwait(false);
            return DataMoveExitCodes.Succeeded;
        }

        if (parsed.Options is not { } options)
        {
            await _error.WriteLineAsync(parsed.Error.AsMemory(), ct).ConfigureAwait(false);
            await _error.WriteLineAsync(DataMoveOptions.Usage.AsMemory(), ct).ConfigureAwait(false);
            return DataMoveExitCodes.UsageError;
        }

        DataMoveReport report;
        try
        {
            report = await _run(options, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _error.WriteLineAsync("Cancelled; every open transaction was rolled back.".AsMemory(), CancellationToken.None).ConfigureAwait(false);
            return DataMoveExitCodes.Failed;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            await _error.WriteLineAsync($"Failed: {exception.Message.TrimEnd('.')}. Every open transaction was rolled back.".AsMemory(), CancellationToken.None).ConfigureAwait(false);
            return DataMoveExitCodes.Failed;
        }

        await _output.WriteLineAsync(DataMoveReportWriter.Render(report).AsMemory(), ct).ConfigureAwait(false);
        return report.ExitCode;
    }
}
