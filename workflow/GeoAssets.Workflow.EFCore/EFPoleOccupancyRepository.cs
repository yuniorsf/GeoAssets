using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IPoleOccupancyReader"/> and
/// <see cref="IPoleOccupancyWriter"/> over the <c>field_pole_occupancies</c> and
/// <c>field_pole_occupancy_entries</c> tables (XD01-314). The contract it honours is the member
/// documentation on those two interfaces; what follows is only how.
///
/// <para><b>Tenancy.</b> Owner only, uniformly: no member calls <c>IgnoreQueryFilters()</c>, so
/// every read and write sees just the poles of the context's organization
/// (<see cref="ServiceOrderDbContext.CurrentOrganizationId"/>), which is also the organization
/// new rows are written under. On PostgreSQL the tables' row-level-security policies are the
/// backstop.</para>
///
/// <para><b>Writes bypass the change tracker.</b> A write is a compare-and-swap on the pole's
/// <c>Version</c> (an insert-if-absent for a pole's first write) followed by replacing its entry
/// rows, inside one transaction: the caller's when the shared context already has one — which
/// is how a later slice commits occupancy together with an order transition — or its own
/// otherwise. Of two connections writing from the same version exactly one changes a row; the
/// other gets <see cref="OccupancyConflictException"/>. When the call throws inside a caller's
/// transaction, rolling that transaction back is the caller's part.</para>
/// </summary>
public sealed class EFPoleOccupancyRepository : IPoleOccupancyReader, IPoleOccupancyWriter
{
    /// <summary>How many ids go into one <c>IN (…)</c> list before a lookup is split.</summary>
    internal const int IdChunkSize = 1000;

    /// <summary>Every mapped column of <see cref="PoleOccupancyRecord"/>, with how to read its value.</summary>
    internal static readonly IReadOnlyList<(string Property, Func<PoleOccupancyRecord, object?> Value)> PoleColumns =
    [
        (nameof(PoleOccupancyRecord.Id), r => r.Id),
        (nameof(PoleOccupancyRecord.PoleFeatureId), r => r.PoleFeatureId),
        (nameof(PoleOccupancyRecord.OrganizationId), r => r.OrganizationId),
        (nameof(PoleOccupancyRecord.Version), r => r.Version),
        (nameof(PoleOccupancyRecord.UpdatedAt), r => r.UpdatedAt),
    ];

    /// <summary>Every mapped column of <see cref="PoleOccupancyEntryRecord"/>, with how to read its value.</summary>
    internal static readonly IReadOnlyList<(string Property, Func<PoleOccupancyEntryRecord, object?> Value)> EntryColumns =
    [
        (nameof(PoleOccupancyEntryRecord.Id), r => r.Id),
        (nameof(PoleOccupancyEntryRecord.PoleOccupancyId), r => r.PoleOccupancyId),
        (nameof(PoleOccupancyEntryRecord.SlotIndex), r => r.SlotIndex),
        (nameof(PoleOccupancyEntryRecord.Status), r => r.Status),
        (nameof(PoleOccupancyEntryRecord.OccupantOrganizationId), r => r.OccupantOrganizationId),
        (nameof(PoleOccupancyEntryRecord.OperatorCnpj), r => r.OperatorCnpj),
        (nameof(PoleOccupancyEntryRecord.AnatelLicenseNumber), r => r.AnatelLicenseNumber),
        (nameof(PoleOccupancyEntryRecord.CableDiameterMm), r => r.CableDiameterMm),
        (nameof(PoleOccupancyEntryRecord.CableLoadDaN), r => r.CableLoadDaN),
        (nameof(PoleOccupancyEntryRecord.AttachedCableFeatureId), r => r.AttachedCableFeatureId),
        (nameof(PoleOccupancyEntryRecord.OrganizationId), r => r.OrganizationId),
    ];

    private static readonly string[] PoleIdentity =
        [nameof(PoleOccupancyRecord.OrganizationId), nameof(PoleOccupancyRecord.PoleFeatureId)];

    private static readonly string ContractedStatus =
        PoleOccupancyMapper.ToStatusText(PoleAttachmentOccupantStatus.AuthorizedContracted);

    private readonly ServiceOrderDbContext _db;
    private readonly TimeProvider _timeProvider;

    /// <param name="db">The scoped context shared with the order repository; its tenant is the pole owner.</param>
    /// <param name="timeProvider">Clock for <see cref="PoleOccupancy.UpdatedAt"/>; the system clock when omitted.</param>
    public EFPoleOccupancyRepository(ServiceOrderDbContext db, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<PoleOccupancy?> GetForPoleAsync(string poleFeatureId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poleFeatureId);
        ct.ThrowIfCancellationRequested();

        var stored = await LoadAsync(Poles.Where(p => p.PoleFeatureId == poleFeatureId), ct).ConfigureAwait(false);
        return stored.Count == 0 ? null : stored[0].Occupancy;
    }

    public async Task<IReadOnlyList<PoleOccupancy>> GetForPolesAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(poleFeatureIds);
        ct.ThrowIfCancellationRequested();

        var found = new List<PoleOccupancy>();
        foreach (var chunk in Distinct(poleFeatureIds).Chunk(IdChunkSize))
        {
            var stored = await LoadAsync(Poles.Where(p => chunk.Contains(p.PoleFeatureId)), ct).ConfigureAwait(false);
            found.AddRange(stored.Select(s => s.Occupancy));
        }

        found.Sort((left, right) => string.CompareOrdinal(left.PoleFeatureId, right.PoleFeatureId));
        return found;
    }

    public async Task<IReadOnlyList<PoleOccupantPresence>> FindContractedOccupantsAsync(
        IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(poleFeatureIds);
        ct.ThrowIfCancellationRequested();

        var contractedStatus = ContractedStatus;
        var polesByOccupant = new Dictionary<Guid, SortedSet<string>>();
        foreach (var chunk in Distinct(poleFeatureIds).Chunk(IdChunkSize))
        {
            var pairs = await (
                    from pole in Poles
                    join entry in Entries on pole.Id equals entry.PoleOccupancyId
                    where chunk.Contains(pole.PoleFeatureId)
                        && entry.Status == contractedStatus
                        && entry.OccupantOrganizationId != null
                    select new { OccupantOrganizationId = entry.OccupantOrganizationId!.Value, pole.PoleFeatureId })
                .Distinct()
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var pair in pairs)
            {
                if (!polesByOccupant.TryGetValue(pair.OccupantOrganizationId, out var poles))
                    polesByOccupant[pair.OccupantOrganizationId] = poles = new SortedSet<string>(StringComparer.Ordinal);
                poles.Add(pair.PoleFeatureId);
            }
        }

        return polesByOccupant
            .OrderBy(occupant => occupant.Key.ToString("D"), StringComparer.Ordinal)
            .Select(occupant => new PoleOccupantPresence(occupant.Key, [.. occupant.Value]))
            .ToList();
    }

    public async Task<PagedResult<PoleOccupancy>> GetPageAsync(PoleOccupancyQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var matches = Poles;
        if (query.OccupantOrganizationId is { } occupant)
            matches = matches.Where(p => Entries.Any(e => e.PoleOccupancyId == p.Id && e.OccupantOrganizationId == occupant));

        var totalCount = await matches.CountAsync(ct).ConfigureAwait(false);
        var skip = query.Skip;
        if (skip >= totalCount)
            return new PagedResult<PoleOccupancy> { Items = [], TotalCount = totalCount };

        var pageIds = await matches
            .OrderBy(p => p.PoleFeatureId)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(query.PageSize)
            .Select(p => p.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        var stored = await LoadAsync(Poles.Where(p => pageIds.Contains(p.Id)), ct).ConfigureAwait(false);
        var items = stored.Select(s => s.Occupancy).ToList();
        items.Sort((left, right) => string.CompareOrdinal(left.PoleFeatureId, right.PoleFeatureId));

        return new PagedResult<PoleOccupancy> { Items = items, TotalCount = totalCount };
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task<PoleOccupancy> ReplaceEntriesAsync(
        string poleFeatureId,
        int expectedVersion,
        IReadOnlyCollection<PoleOccupancyEntry> entries,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poleFeatureId);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);
        ct.ThrowIfCancellationRequested();

        var stored = (await LoadAsync(Poles.Where(p => p.PoleFeatureId == poleFeatureId), ct).ConfigureAwait(false))
            .FirstOrDefault();
        if ((stored?.Occupancy.Version ?? PoleOccupancy.UnsavedVersion) != expectedVersion)
            throw new OccupancyConflictException([poleFeatureId]);

        var now = WholeMicroseconds(_timeProvider.GetUtcNow());
        var next = stored is null
            ? PoleOccupancy.Create(poleFeatureId, _db.CurrentOrganizationId, entries, PoleOccupancy.FirstVersion, now)
            : stored.Occupancy.Revise(entries, now);

        if (_db.Database.CurrentTransaction is not null)
        {
            await WriteAsync(stored?.Id, next, ct).ConfigureAwait(false);
            return next;
        }

        var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await WriteAsync(stored?.Id, next, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return next;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IQueryable<PoleOccupancyRecord> Poles => _db.PoleOccupancies.AsNoTracking();

    private IQueryable<PoleOccupancyEntryRecord> Entries => _db.PoleOccupancyEntries.AsNoTracking();

    private static string[] Distinct(IReadOnlyCollection<string> poleFeatureIds) =>
        poleFeatureIds.Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// PostgreSQL keeps microseconds; the pole handed back by a write must equal the pole read
    /// back afterwards, so the clock's sub-microsecond ticks are dropped before either happens.
    /// </summary>
    private static DateTimeOffset WholeMicroseconds(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);
    }

    /// <summary>
    /// The selected poles with all their entries, in one statement (a left join), so a pole and
    /// its entries always come from the same committed version.
    /// </summary>
    private async Task<List<StoredPole>> LoadAsync(IQueryable<PoleOccupancyRecord> poles, CancellationToken ct)
    {
        var rows = await (
                from pole in poles
                join entry in Entries on pole.Id equals entry.PoleOccupancyId into poleEntries
                from entry in poleEntries.DefaultIfEmpty()
                select new { Pole = pole, Entry = entry })
            .ToListAsync(ct).ConfigureAwait(false);

        return rows
            .GroupBy(row => row.Pole.Id)
            .Select(group =>
            {
                var pole = group.First().Pole;
                var entries = group.Where(row => row.Entry is not null).Select(row => row.Entry!);
                return new StoredPole(pole.Id, PoleOccupancyMapper.ToDomain(pole, entries));
            })
            .ToList();
    }

    /// <summary>
    /// The compare-and-swap and the entry replacement. Runs inside a transaction (the caller's
    /// or <see cref="ReplaceEntriesAsync"/>'s own); the version step comes first, so a lost race
    /// throws before anything was changed.
    /// </summary>
    private async Task WriteAsync(Guid? storedId, PoleOccupancy next, CancellationToken ct)
    {
        Guid poleId;
        if (storedId is null)
        {
            // First write of the pole: another first writer either already committed its row
            // (this insert is skipped) or commits it while this statement waits on the unique index.
            poleId = Guid.NewGuid();
            var inserted = await RawRowInsert
                .ExecuteAsync(_db, [PoleOccupancyMapper.ToRecord(next, poleId)], PoleColumns, PoleIdentity, ct)
                .ConfigureAwait(false);
            if (inserted == 0)
                throw new OccupancyConflictException([next.PoleFeatureId]);
        }
        else
        {
            poleId = storedId.Value;
            var expectedVersion = next.Version - 1;
            var nextVersion = next.Version;
            var updatedAt = next.UpdatedAt.UtcDateTime;
            var updated = await _db.PoleOccupancies
                .Where(p => p.Id == poleId && p.Version == expectedVersion)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(p => p.Version, nextVersion)
                        .SetProperty(p => p.UpdatedAt, updatedAt),
                    ct).ConfigureAwait(false);
            if (updated == 0)
                throw new OccupancyConflictException([next.PoleFeatureId]);

            await _db.PoleOccupancyEntries
                .Where(e => e.PoleOccupancyId == poleId)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }

        var records = next.Entries.Select(entry => PoleOccupancyMapper.ToEntryRecord(entry, poleId)).ToList();
        await RawRowInsert.ExecuteAsync(_db, records, EntryColumns, skipOnConflict: null, ct).ConfigureAwait(false);
    }

    private sealed record StoredPole(Guid Id, PoleOccupancy Occupancy);
}
