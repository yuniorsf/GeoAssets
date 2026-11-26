namespace GeoAssets.Tools.NetworkImport;

/// <summary>The tool's process exit codes.</summary>
internal static class NetworkImportExitCodes
{
    public const int Succeeded = 0;

    /// <summary>Unknown verb or option, a missing value or required option, missing credentials.</summary>
    public const int UsageError = 1;

    /// <summary>The delivery folder is missing, the profile unknown, an organization id or URL invalid…</summary>
    public const int InputError = 2;

    /// <summary>The kernel answered a non-success status (after retries) or could not be reached; verify mismatches.</summary>
    public const int KernelFailure = 3;
}
