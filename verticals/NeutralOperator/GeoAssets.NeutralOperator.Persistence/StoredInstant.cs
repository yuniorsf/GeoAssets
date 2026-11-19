namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// The domain's instants are UTC. SQLite hands a stored one back with <see cref="DateTimeKind.Unspecified"/> and
/// PostgreSQL's <c>timestamp with time zone</c> rejects anything but <see cref="DateTimeKind.Utc"/>, so reads normalise the
/// kind — the rule the kernel's target mapper applies to its own columns.
/// </summary>
internal static class StoredInstant
{
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
