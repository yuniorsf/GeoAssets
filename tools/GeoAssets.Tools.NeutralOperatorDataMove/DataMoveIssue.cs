namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>How much an issue stops.</summary>
internal enum DataMoveIssueSeverity
{
    /// <summary>For the record; changes nothing.</summary>
    Info,

    /// <summary>Worth reading before the next step; changes nothing.</summary>
    Warning,

    /// <summary>Data that cannot move as it is (exit 2; <c>copy</c> writes nothing, <c>remove</c> refuses).</summary>
    Conflict,

    /// <summary>A precondition failed (exit 3); nothing was changed.</summary>
    Refusal,
}

/// <summary>One finding of a run, with a stable <paramref name="Code"/> (e.g. <c>rls.filtered</c>) and a message without secrets.</summary>
internal sealed record DataMoveIssue(DataMoveIssueSeverity Severity, string Code, string Message);
