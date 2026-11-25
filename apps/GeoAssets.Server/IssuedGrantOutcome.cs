namespace GeoAssets.Server;

/// <summary>What an issued-grant write did (XD01-411).</summary>
public enum IssuedGrantOutcome
{
    Created,
    Updated,
    Unchanged,
    Revoked,

    /// <summary>The grant does not exist, is inactive or expired, or another organization issued it.</summary>
    NotFound,

    /// <summary>An active grant with the requested key already exists and an administrator owns it.</summary>
    Exists,

    /// <summary>The grant, or the active grant with the requested key, is owned by a vertical host.</summary>
    ServiceOwned,
}
