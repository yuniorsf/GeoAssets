using System.Collections.Concurrent;
using GeoAssets.Domain.NeutralPole.SlotAllocation;
using GeoAssets.Domain.NeutralPole.UnauthorizedCableNotice;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Process-lifetime, in-memory store of each pole's <see cref="PoleSlotAllocationBoard"/> and
/// <see cref="CaboAReveliaNotice"/>s, keyed by pole id.
///
/// GeoAssets.Domain.NeutralPole is deliberately persistence-agnostic (XD01-211), so this host
/// needs <i>something</i> to hold state across requests; a real, durable store (almost
/// certainly backed by the same Postgres database every other tenant-scoped table in this host
/// uses, given the AC on XD01-211 about competitor-tenant isolation) is its own follow-up
/// ticket. Registered as a singleton — state does not survive a restart, and is shared across
/// every tenant's requests in this process, which is acceptable for exercising the endpoints
/// but not for production multi-tenant use.
/// </summary>
internal sealed class NeutralPoleRegistry
{
    private readonly ConcurrentDictionary<string, PoleSlotAllocationBoard> _boards = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CaboAReveliaNotice> _notices = new(StringComparer.Ordinal);

    public PoleSlotAllocationBoard GetOrCreateBoard(string poleId) =>
        _boards.GetOrAdd(poleId, _ => new PoleSlotAllocationBoard());

    public CaboAReveliaNotice? GetNotice(string poleId) => _notices.GetValueOrDefault(poleId);

    public CaboAReveliaNotice GetOrCreateNotice(string poleId, DateTimeOffset detectedAt) =>
        _notices.GetOrAdd(poleId, _ => new CaboAReveliaNotice(poleId, detectedAt));
}
