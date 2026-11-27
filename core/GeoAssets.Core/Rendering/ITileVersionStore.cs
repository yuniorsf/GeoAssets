namespace GeoAssets.Core.Rendering;

/// <summary>
/// Shared storage port for tile URL versions (ADR-002 D6, XD01-458): the epoch, the flush count and the per-scope
/// generations every kernel replica over one database publishes in <c>{v}</c>, durable so that a restarted or new replica
/// starts at the current values, and announced so that every replica sees another's change within a bounded delay. A
/// scope is an organization id; <see cref="Guid.Empty"/> is the organization-less (shared) features' scope. Every value
/// only ever grows. Implementations are thread-safe.
/// </summary>
public interface ITileVersionStore
{
    /// <summary>
    /// The shared epoch and flush count (the epoch chosen at random once, on first use) and the generations of
    /// <paramref name="scopes"/> (<c>0</c> for a scope never advanced).
    /// </summary>
    Task<TileVersionSnapshot> LoadAsync(IReadOnlyCollection<Guid> scopes, CancellationToken ct);

    /// <summary>
    /// Advances the generation of each of <paramref name="scopes"/> by one and, when <paramref name="flush"/>, the flush
    /// count — atomically — and announces the change, with <paramref name="regions"/> (the write's dirty tiles), to every
    /// listener as coming from <paramref name="origin"/>. Answers the change as announced: the new values.
    /// </summary>
    Task<TileVersionChange> AdvanceAsync(string origin, IReadOnlyCollection<Guid> scopes, bool flush, IReadOnlyCollection<DirtyTileRegion> regions, CancellationToken ct);

    /// <summary>
    /// One subscription to the announced changes: once subscribed it awaits <paramref name="subscribed"/> (every change
    /// announced after it started reaches <paramref name="changed"/>, in order), then hands each change to
    /// <paramref name="changed"/> until <paramref name="ct"/> is cancelled. Throws when the subscription is lost — the
    /// changes announced meanwhile are lost with it, so the caller subscribes again and treats the gap as unknown.
    /// </summary>
    Task ListenAsync(Func<CancellationToken, Task> subscribed, Func<TileVersionChange, CancellationToken, Task> changed, CancellationToken ct);
}
