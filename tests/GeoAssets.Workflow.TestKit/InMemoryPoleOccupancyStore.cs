using GeoAssets.Core.Interfaces;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Dictionary-backed <see cref="IPoleOccupancyReader"/> + <see cref="IPoleOccupancyWriter"/>
/// (XD01-314) for tests that need a working occupancy store without a database. It passes the
/// same <see cref="PoleOccupancyStoreContractTests"/> the EF Core store does, so a service
/// tested against it behaves the same against the real one.
///
/// Like the real store it is scoped to one organization (the pole owner), given by the tenant
/// context; <see cref="ForOrganization"/> hands out another view over the <b>same</b> data
/// scoped to a different organization, which is how a test shows that an occupant, or any other
/// tenant, reads nothing of the owner's.
/// </summary>
public sealed class InMemoryPoleOccupancyStore : IPoleOccupancyReader, IPoleOccupancyWriter
{
    private readonly State _state;
    private readonly ITenantContext _tenantContext;

    /// <param name="tenantContext">The pole owner this view is scoped to; <see cref="Guid.Empty"/> when omitted.</param>
    /// <param name="timeProvider">Clock for <see cref="PoleOccupancy.UpdatedAt"/>; the system clock when omitted.</param>
    public InMemoryPoleOccupancyStore(ITenantContext? tenantContext = null, TimeProvider? timeProvider = null)
        : this(new State(timeProvider ?? TimeProvider.System), tenantContext ?? NullTenantContext.Instance)
    {
    }

    private InMemoryPoleOccupancyStore(State state, ITenantContext tenantContext)
    {
        _state = state;
        _tenantContext = tenantContext;
    }

    /// <summary>A view over the same stored data, scoped to <paramref name="organizationId"/>.</summary>
    public InMemoryPoleOccupancyStore ForOrganization(Guid organizationId) =>
        new(_state, new FixedTenant(organizationId));

    /// <summary>Every stored pole of every organization, in no particular order — for assertions.</summary>
    public IReadOnlyList<PoleOccupancy> Snapshot()
    {
        lock (_state.Gate) return [.. _state.Poles.Values];
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public Task<PoleOccupancy?> GetForPoleAsync(string poleFeatureId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poleFeatureId);
        ct.ThrowIfCancellationRequested();

        lock (_state.Gate)
            return Task.FromResult(_state.Poles.GetValueOrDefault((_tenantContext.CurrentOrganizationId, poleFeatureId)));
    }

    public Task<IReadOnlyList<PoleOccupancy>> GetForPolesAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(poleFeatureIds);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<PoleOccupancy>>(Find(poleFeatureIds));
    }

    public Task<IReadOnlyList<PoleOccupantPresence>> FindContractedOccupantsAsync(
        IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(poleFeatureIds);
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<PoleOccupantPresence> presences = Find(poleFeatureIds)
            .SelectMany(pole => pole.Entries.Where(entry => entry.IsContracted))
            .GroupBy(entry => entry.OccupantOrganizationId.GetValueOrDefault())
            .OrderBy(occupant => occupant.Key.ToString("D"), StringComparer.Ordinal)
            .Select(occupant => new PoleOccupantPresence(
                occupant.Key,
                [.. occupant.Select(entry => entry.PoleFeatureId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]))
            .ToList();
        return Task.FromResult(presences);
    }

    public Task<PagedResult<PoleOccupancy>> GetPageAsync(PoleOccupancyQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        List<PoleOccupancy> matches;
        lock (_state.Gate)
        {
            matches = _state.Poles.Values
                .Where(pole => pole.PoleOwnerOrganizationId == _tenantContext.CurrentOrganizationId)
                .Where(pole => query.OccupantOrganizationId is not { } occupant
                    || pole.Entries.Any(entry => entry.OccupantOrganizationId == occupant))
                .OrderBy(pole => pole.PoleFeatureId, StringComparer.Ordinal)
                .ToList();
        }

        return Task.FromResult(new PagedResult<PoleOccupancy>
        {
            Items = matches.Skip(query.Skip).Take(query.PageSize).ToList(),
            TotalCount = matches.Count,
        });
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public Task<PoleOccupancy> ReplaceEntriesAsync(
        string poleFeatureId,
        int expectedVersion,
        IReadOnlyCollection<PoleOccupancyEntry> entries,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poleFeatureId);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);
        ct.ThrowIfCancellationRequested();

        var organizationId = _tenantContext.CurrentOrganizationId;
        lock (_state.Gate)
        {
            var stored = _state.Poles.GetValueOrDefault((organizationId, poleFeatureId));
            if ((stored?.Version ?? PoleOccupancy.UnsavedVersion) != expectedVersion)
                throw new OccupancyConflictException([poleFeatureId]);

            var now = _state.TimeProvider.GetUtcNow();
            var next = stored is null
                ? PoleOccupancy.Create(poleFeatureId, organizationId, entries, PoleOccupancy.FirstVersion, now)
                : stored.Revise(entries, now);

            _state.Poles[(organizationId, poleFeatureId)] = next;
            return Task.FromResult(next);
        }
    }

    /// <summary>
    /// The approval saga's compensation (XD01-383), as the EF store's <c>TryRestoreEntriesAsync</c> does it: puts
    /// <paramref name="entries"/> back on the pole the approval left at <paramref name="expectedVersion"/>, as the next
    /// version, without the status-move checks of a revision. Answers null, changing nothing, when the pole is no longer at
    /// that version (XD01-392).
    /// </summary>
    public PoleOccupancy? TryRestoreEntries(string poleFeatureId, int expectedVersion, IReadOnlyCollection<PoleOccupancyEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poleFeatureId);
        ArgumentNullException.ThrowIfNull(entries);

        var organizationId = _tenantContext.CurrentOrganizationId;
        lock (_state.Gate)
        {
            var stored = _state.Poles.GetValueOrDefault((organizationId, poleFeatureId));
            if (stored is null || stored.Version != expectedVersion)
                return null;

            var next = PoleOccupancy.Create(poleFeatureId, organizationId, entries, expectedVersion + 1, _state.TimeProvider.GetUtcNow());
            _state.Poles[(organizationId, poleFeatureId)] = next;
            return next;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private List<PoleOccupancy> Find(IReadOnlyCollection<string> poleFeatureIds)
    {
        var organizationId = _tenantContext.CurrentOrganizationId;
        var wanted = poleFeatureIds.Where(id => id is not null).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

        lock (_state.Gate)
        {
            return wanted
                .Select(id => _state.Poles.GetValueOrDefault((organizationId, id)))
                .OfType<PoleOccupancy>()
                .ToList();
        }
    }

    private sealed class State(TimeProvider timeProvider)
    {
        public Lock Gate { get; } = new();

        public Dictionary<(Guid OrganizationId, string PoleFeatureId), PoleOccupancy> Poles { get; } = [];

        public TimeProvider TimeProvider { get; } = timeProvider;
    }

    private sealed class FixedTenant(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
