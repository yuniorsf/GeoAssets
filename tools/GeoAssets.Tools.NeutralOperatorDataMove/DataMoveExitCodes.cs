namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>The tool's process exit codes (see <see cref="DataMoveOptions.Usage"/>).</summary>
internal static class DataMoveExitCodes
{
    /// <summary>The verb ran to completion and found nothing wrong.</summary>
    public const int Succeeded = 0;

    /// <summary>The arguments are wrong; no database was opened.</summary>
    public const int UsageError = 1;

    /// <summary>A moved row conflicts with the target (an id present with different content), or — <c>verify</c> — a row is missing or differs.</summary>
    public const int DifferencesFound = 2;

    /// <summary>A precondition failed (migrations, schema, row-level security, privileges, pending outbox) or <c>remove</c> refused; nothing was changed.</summary>
    public const int Refused = 3;

    /// <summary>A database call failed or the run was cancelled; every open transaction was rolled back.</summary>
    public const int Failed = 4;
}
