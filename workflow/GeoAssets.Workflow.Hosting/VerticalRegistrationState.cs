namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Where this host's bootstrap registration with the kernel stands (XD01-382): pending until the order-type
/// descriptors (if any) and the permissions were published, failing while the last attempt failed, registered once all succeeded.
/// Written by <see cref="VerticalRegistrationService"/>, read by <see cref="VerticalRegistrationHealthCheck"/>; thread-safe.
/// </summary>
public sealed class VerticalRegistrationState
{
    private readonly Lock _gate = new();
    private VerticalRegistrationSnapshot _current = new(VerticalRegistrationStatus.Pending, 0, null, null);

    public VerticalRegistrationSnapshot Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public void Registered(DateTimeOffset at)
    {
        lock (_gate) _current = new VerticalRegistrationSnapshot(VerticalRegistrationStatus.Registered, 0, null, at);
    }

    public void Failed(string error)
    {
        lock (_gate) _current = _current with { Status = VerticalRegistrationStatus.Failing, ConsecutiveFailures = _current.ConsecutiveFailures + 1, LastError = error };
    }
}
