using System.Globalization;
using System.Text;

namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>Renders a <see cref="DataMoveReport"/> as the plain text the tool prints.</summary>
internal static class DataMoveReportWriter
{
    public static string Render(DataMoveReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        text.Append(culture, $"NeutralOperator data move: {Verb(report.Verb)}").AppendLine();
        text.Append(culture, $"  kernel database (source):           {report.Kernel}").AppendLine();
        text.Append(culture, $"  NeutralOperator database (target):  {report.Target}").AppendLine();
        text.Append(culture, $"  order types moved: {string.Join(", ", report.OrderTypes)}").AppendLine();

        if (report.Tables.Count > 0)
        {
            text.AppendLine();
            text.Append(culture, $"{"Table",-48} {"Source",9} {"Identical",9} {"Missing",9} {"Differ",9} {"Copied",9} {"Removed",9}").AppendLine();
            foreach (var table in report.Tables)
            {
                text.Append(culture, $"{table.Table,-48} {table.SourceRows,9} {table.Identical,9} {table.Missing,9} {table.Different,9} {table.Copied,9} {table.Removed,9}").AppendLine();
                if (table.DifferentKeys.Count > 0)
                    text.Append(culture, $"    differs: {string.Join("; ", table.DifferentKeys)}").AppendLine();
            }
        }

        if (report.Issues.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Issues:");
            foreach (var issue in report.Issues.OrderByDescending(i => i.Severity))
                text.Append(culture, $"  {issue.Severity.ToString().ToUpperInvariant(),-8} {issue.Code}: {issue.Message}").AppendLine();
        }

        text.AppendLine();
        text.Append(culture, $"Result: exit {report.ExitCode} — {Outcome(report)}").AppendLine();
        return text.ToString();
    }

    private static string Verb(DataMoveVerb verb) => verb switch
    {
        DataMoveVerb.Plan => "plan (dry run, nothing written)",
        DataMoveVerb.Copy => "copy",
        DataMoveVerb.Verify => "verify (nothing written)",
        DataMoveVerb.Remove => "remove",
        _ => verb.ToString(),
    };

    private static string Outcome(DataMoveReport report) => report.ExitCode switch
    {
        DataMoveExitCodes.Refused => report.Changed
            ? "refused part-way; see the issues"
            : "refused; nothing was changed",
        DataMoveExitCodes.DifferencesFound => report.Verb == DataMoveVerb.Copy && report.Changed
            ? "copied what it could; conflicts found (nothing was overwritten)"
            : "conflicts or differences found; nothing was overwritten",
        _ => report.Verb switch
        {
            DataMoveVerb.Copy => report.Changed ? "copied" : "nothing to copy (the target already holds every row)",
            DataMoveVerb.Verify => "every moved row is in the target, identical",
            DataMoveVerb.Remove => "removed from the kernel's database",
            _ => "ready",
        },
    };
}
