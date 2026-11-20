namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>Everything one run found and did, and the exit code that follows from it.</summary>
internal sealed class DataMoveReport(DataMoveVerb verb, IReadOnlyList<string> orderTypes, string kernel, string target)
{
    private readonly List<DataMoveIssue> _issues = [];
    private readonly List<TableOutcome> _tables = [];

    public DataMoveVerb Verb { get; } = verb;

    /// <summary>The NeutralOperator order type ids whose orders move.</summary>
    public IReadOnlyList<string> OrderTypes { get; } = orderTypes;

    /// <summary>The kernel's database as <c>host:port/database</c>.</summary>
    public string Kernel { get; } = kernel;

    /// <summary>NeutralOperator.Server's database as <c>host:port/database</c>.</summary>
    public string Target { get; } = target;

    public IReadOnlyList<DataMoveIssue> Issues => _issues;

    public IReadOnlyList<TableOutcome> Tables => _tables;

    /// <summary>Whether the run's writes were committed (<c>copy</c>: at least one batch; <c>remove</c>: the one transaction).</summary>
    public bool Changed { get; set; }

    public bool HasRefusal => _issues.Exists(i => i.Severity == DataMoveIssueSeverity.Refusal);

    public bool HasConflict => _issues.Exists(i => i.Severity == DataMoveIssueSeverity.Conflict) || _tables.Exists(t => t.Different > 0);

    public bool HasMissing => _tables.Exists(t => t.Missing > 0);

    /// <summary>
    /// <see cref="DataMoveExitCodes.Refused"/> on any refusal; else <see cref="DataMoveExitCodes.DifferencesFound"/> on a
    /// conflict or differing row — or, for <c>verify</c>, a missing one; else <see cref="DataMoveExitCodes.Succeeded"/>.
    /// </summary>
    public int ExitCode =>
        HasRefusal ? DataMoveExitCodes.Refused
        : HasConflict || (Verb == DataMoveVerb.Verify && HasMissing) ? DataMoveExitCodes.DifferencesFound
        : DataMoveExitCodes.Succeeded;

    public void Add(DataMoveIssueSeverity severity, string code, string message) => _issues.Add(new DataMoveIssue(severity, code, message));

    public void SetTables(IEnumerable<TableOutcome> tables)
    {
        _tables.Clear();
        _tables.AddRange(tables);
    }
}
