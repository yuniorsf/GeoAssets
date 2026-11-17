using System.Globalization;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// One run of the tool: parse the arguments, read and check the input, plan, import (or compare,
/// on a dry run), write the report. The store is opened through <paramref name="openStore"/>, so
/// the whole run can be exercised without a database.
/// </summary>
internal sealed class SharedUseImportApplication(
    Func<SharedUseImportOptions, OccupancyStoreSession> openStore,
    TimeProvider timeProvider,
    TextWriter output,
    TextWriter error)
{
    /// <summary>The run finished and the report was written.</summary>
    public const int Succeeded = 0;

    /// <summary>The arguments are wrong; nothing was read.</summary>
    public const int UsageError = 1;

    /// <summary>An input file is missing or malformed; nothing was written.</summary>
    public const int InputError = 2;

    private readonly Func<SharedUseImportOptions, OccupancyStoreSession> _openStore = openStore ?? throw new ArgumentNullException(nameof(openStore));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly TextWriter _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly TextWriter _error = error ?? throw new ArgumentNullException(nameof(error));

    public async Task<int> RunAsync(IReadOnlyList<string> args, string? connectionString, CancellationToken ct)
    {
        var (options, usageError) = SharedUseImportCommandLine.Parse(args, connectionString);
        if (options is null)
        {
            await _error.WriteLineAsync(usageError.AsMemory(), ct).ConfigureAwait(false);
            await _error.WriteLineAsync(SharedUseImportOptions.Usage.AsMemory(), ct).ConfigureAwait(false);
            return UsageError;
        }

        SharedUseImportPlan plan;
        try
        {
            plan = await PlanAsync(options, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
        {
            await _error.WriteLineAsync(exception.Message.AsMemory(), ct).ConfigureAwait(false);
            return InputError;
        }

        SharedUseImportReport report;
        var session = _openStore(options);
        await using (session.ConfigureAwait(false))
        {
            report = await new SharedUseImporter(session.Reader, session.Writer, _timeProvider)
                .ImportAsync(plan, options.DryRun, ct).ConfigureAwait(false);
        }

        var stream = File.Create(options.ReportPath);
        await using (stream.ConfigureAwait(false))
            await SharedUseImportJson.WriteReportAsync(stream, report, ct).ConfigureAwait(false);

        await _output.WriteLineAsync(Summary(report, options.ReportPath).AsMemory(), ct).ConfigureAwait(false);
        return Succeeded;
    }

    private async Task<SharedUseImportPlan> PlanAsync(SharedUseImportOptions options, CancellationToken ct)
    {
        IReadOnlyList<LegacySharedUseRow> rows;
        using (var reader = new StreamReader(options.SharedUsesPath))
            rows = await LegacySharedUseCsvParser.ParseAsync(reader, ct).ConfigureAwait(false);

        SharedUseImportMapping mapping;
        var stream = File.OpenRead(options.MappingPath);
        await using (stream.ConfigureAwait(false))
            mapping = await SharedUseImportJson.ReadMappingAsync(stream, ct).ConfigureAwait(false);

        return SharedUseImportPlanner.Plan(rows, mapping, options.OrganizationId, _timeProvider.GetUtcNow());
    }

    private static string Summary(SharedUseImportReport report, string reportPath)
    {
        var totals = report.Totals;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(report.DryRun ? "Dry run" : "Import")}: {totals.RowsRead} rows read, {totals.RowsSkipped} skipped, " +
            $"{totals.RowsImported} imported, {totals.RowsFlagged} flagged; {totals.Poles} poles, " +
            $"{totals.PolesImported} imported, {totals.PolesIrregular} irregular. Report: {reportPath}");
    }
}
