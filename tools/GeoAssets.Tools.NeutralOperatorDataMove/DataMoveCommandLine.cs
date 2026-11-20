using Npgsql;

namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>The outcome of <see cref="DataMoveCommandLine.Parse"/>: options, a usage error, or a request for help.</summary>
internal sealed record DataMoveCommandLineResult(DataMoveOptions? Options, string? Error, bool HelpRequested)
{
    public static DataMoveCommandLineResult Help { get; } = new(null, null, true);

    public static DataMoveCommandLineResult Invalid(string error) => new(null, error, false);
}

/// <summary>Parses the tool's arguments (pure: the environment variables are passed in).</summary>
internal static class DataMoveCommandLine
{
    private const string Source = "--source";
    private const string Target = "--target";
    private const string BatchSize = "--batch-size";
    private const string IncludePendingOutbox = "--include-pending-outbox";
    private const string Confirm = "--confirm";

    private static readonly Dictionary<string, DataMoveVerb> Verbs = new(StringComparer.Ordinal)
    {
        ["plan"] = DataMoveVerb.Plan,
        ["copy"] = DataMoveVerb.Copy,
        ["verify"] = DataMoveVerb.Verify,
        ["remove"] = DataMoveVerb.Remove,
    };

    /// <param name="args">The command-line arguments.</param>
    /// <param name="sourceVariable">The value of <see cref="DataMoveOptions.SourceVariable"/>, if set.</param>
    /// <param name="targetVariable">The value of <see cref="DataMoveOptions.TargetVariable"/>, if set.</param>
    public static DataMoveCommandLineResult Parse(IReadOnlyList<string> args, string? sourceVariable, string? targetVariable)
    {
        ArgumentNullException.ThrowIfNull(args);

        DataMoveVerb? verb = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h")
                return DataMoveCommandLineResult.Help;

            if (!arg.StartsWith('-'))
            {
                if (verb is not null) return DataMoveCommandLineResult.Invalid($"Unexpected argument '{arg}': give one verb.");
                if (!Verbs.TryGetValue(arg, out var parsed))
                    return DataMoveCommandLineResult.Invalid($"Unknown verb '{arg}' (plan, copy, verify or remove).");
                verb = parsed;
                continue;
            }

            if (arg is IncludePendingOutbox or Confirm)
            {
                if (!flags.Add(arg)) return DataMoveCommandLineResult.Invalid($"{arg} is given more than once.");
                continue;
            }

            if (arg is not (Source or Target or BatchSize))
                return DataMoveCommandLineResult.Invalid($"Unknown option '{arg}'.");
            if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return DataMoveCommandLineResult.Invalid($"{arg} needs a value.");
            if (!values.TryAdd(arg, args[++i]))
                return DataMoveCommandLineResult.Invalid($"{arg} is given more than once.");
        }

        var chosen = verb ?? DataMoveVerb.Plan;
        if (flags.Contains(Confirm) && chosen != DataMoveVerb.Remove)
            return DataMoveCommandLineResult.Invalid($"{Confirm} belongs to remove only.");
        if (chosen == DataMoveVerb.Remove && !flags.Contains(Confirm))
            return DataMoveCommandLineResult.Invalid($"remove deletes the moved rows from the kernel's database: pass {Confirm} to do it.");
        if (flags.Contains(IncludePendingOutbox) && chosen is not (DataMoveVerb.Plan or DataMoveVerb.Copy))
            return DataMoveCommandLineResult.Invalid($"{IncludePendingOutbox} belongs to plan and copy only.");

        var batchSize = DataMoveOptions.DefaultBatchSize;
        if (values.TryGetValue(BatchSize, out var batch)
            && (!int.TryParse(batch, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out batchSize)
                || batchSize < 1 || batchSize > DataMoveOptions.MaxBatchSize))
        {
            return DataMoveCommandLineResult.Invalid($"{BatchSize} must be a whole number from 1 to {DataMoveOptions.MaxBatchSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        var source = values.GetValueOrDefault(Source) ?? sourceVariable;
        var target = values.GetValueOrDefault(Target) ?? targetVariable;
        if (string.IsNullOrWhiteSpace(source))
            return DataMoveCommandLineResult.Invalid($"The kernel's database is required: {Source} or {DataMoveOptions.SourceVariable}.");
        if (string.IsNullOrWhiteSpace(target))
            return DataMoveCommandLineResult.Invalid($"NeutralOperator.Server's database is required: {Target} or {DataMoveOptions.TargetVariable}.");

        var sourceDatabase = DatabaseOf(source);
        if (sourceDatabase is null) return DataMoveCommandLineResult.Invalid("The kernel's connection string is not a valid PostgreSQL connection string.");
        var targetDatabase = DatabaseOf(target);
        if (targetDatabase is null) return DataMoveCommandLineResult.Invalid("NeutralOperator.Server's connection string is not a valid PostgreSQL connection string.");
        if (string.Equals(sourceDatabase, targetDatabase, StringComparison.OrdinalIgnoreCase))
            return DataMoveCommandLineResult.Invalid("The kernel's and NeutralOperator.Server's connection strings name the same database.");

        return new DataMoveCommandLineResult(
            new DataMoveOptions
            {
                Verb = chosen,
                SourceConnectionString = source,
                TargetConnectionString = target,
                BatchSize = batchSize,
                IncludePendingOutbox = flags.Contains(IncludePendingOutbox),
            },
            null,
            false);
    }

    /// <summary>Host, port and database of <paramref name="connectionString"/> — never its credentials — or null when it does not parse.</summary>
    internal static string? DatabaseOf(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return PostgresSession.Describe(builder);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
