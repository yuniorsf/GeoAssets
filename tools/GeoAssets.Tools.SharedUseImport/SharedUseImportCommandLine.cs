namespace GeoAssets.Tools.SharedUseImport;

/// <summary>Parses the tool's arguments (pure).</summary>
internal static class SharedUseImportCommandLine
{
    private const string SharedUses = "--shared-uses";
    private const string Mapping = "--mapping";
    private const string Report = "--report";
    private const string Organization = "--organization";
    private const string DryRun = "--dry-run";

    /// <summary>
    /// The options in <paramref name="args"/>, or an error message. Every option but
    /// <c>--dry-run</c> takes a value and must appear exactly once.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="connectionString">The value of <see cref="SharedUseImportOptions.ConnectionStringVariable"/>.</param>
    public static (SharedUseImportOptions? Options, string? Error) Parse(IReadOnlyList<string> args, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(args);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var dryRun = false;
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            if (string.Equals(name, DryRun, StringComparison.Ordinal))
            {
                if (dryRun) return (null, $"{DryRun} is given more than once.");
                dryRun = true;
                continue;
            }

            if (name is not (SharedUses or Mapping or Report or Organization))
                return (null, $"Unknown argument '{name}'.");
            if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return (null, $"{name} needs a value.");
            if (!values.TryAdd(name, args[++i]))
                return (null, $"{name} is given more than once.");
        }

        if (new[] { SharedUses, Mapping, Organization, Report }.FirstOrDefault(name => !values.ContainsKey(name)) is { } missing)
            return (null, $"{missing} is required.");
        if (!Guid.TryParse(values[Organization], out var organizationId) || organizationId == Guid.Empty)
            return (null, $"{Organization} must be a non-empty organization id (GUID).");
        if (string.IsNullOrWhiteSpace(connectionString))
            return (null, $"Set {SharedUseImportOptions.ConnectionStringVariable} to the PostgreSQL connection string.");

        return (new SharedUseImportOptions(values[SharedUses], values[Mapping], values[Report], organizationId, dryRun, connectionString), null);
    }
}
