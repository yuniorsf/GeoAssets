namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// Where this host's bootstrap registration with the kernel stands (XD01-382): pending until both the order-type
/// descriptors and the permissions were published, failing while the last attempt failed, registered once both succeeded.
/// Written by <see cref="VerticalRegistrationService"/>, read by <see cref="VerticalRegistrationHealthCheck"/>; thread-safe.
/// </summary>
internal sealed class VerticalRegistrationState
{
    private readonly Lock _gate = new();
    private Snapshot _current = new(VerticalRegistrationStatus.Pending, 0, null, null);

    public Snapshot Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public void Registered(DateTimeOffset at)
    {
        lock (_gate) _current = new Snapshot(VerticalRegistrationStatus.Registered, 0, null, at);
    }

    public void Failed(string error)
    {
        lock (_gate) _current = _current with { Status = VerticalRegistrationStatus.Failing, ConsecutiveFailures = _current.ConsecutiveFailures + 1, LastError = error };
    }

    /// <summary>One reading of the state.</summary>
    /// <param name="Status">Where the registration stands.</param>
    /// <param name="ConsecutiveFailures">Failed attempts since the last success.</param>
    /// <param name="LastError">Why the last attempt failed; null after a success.</param>
    /// <param name="RegisteredAt">When the registration last succeeded; null before the first success.</param>
    internal sealed record Snapshot(VerticalRegistrationStatus Status, int ConsecutiveFailures, string? LastError, DateTimeOffset? RegisteredAt);
}

