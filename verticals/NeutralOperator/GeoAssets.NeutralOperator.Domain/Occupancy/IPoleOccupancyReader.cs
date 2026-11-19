using GeoAssets.Core.Models;

namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>
/// Read side of the pole occupancy store (XD01-314).
///
/// <para><b>Adapter contract, common to every member.</b></para>
/// <list type="bullet">
///   <item><description><b>Owner only.</b> An adapter is scoped to one organization — the
///   caller's (<c>ITenantContext.CurrentOrganizationId</c>) — and only ever returns poles that
///   organization owns. Occupancy is the pole owner's data: an occupant organization never reads
///   it through this port, not even the entries that name it, and no grant widens that.</description></item>
///   <item><description><b>Whole poles.</b> A returned <see cref="PoleOccupancy"/> carries every
///   entry of the pole, over-capacity entries included, exactly as last written, with the
///   <see cref="PoleOccupancy.Version"/> that write produced. A pole that was written with no
///   entries is on file (returned, empty); a pole that was never written is not.</description></item>
///   <item><description><b>Ordinal text.</b> Pole feature ids are compared and ordered ordinally
///   (case-sensitive, byte-wise), on every adapter.</description></item>
///   <item><description><b>Cancellation.</b> A token already cancelled on entry throws
///   <see cref="OperationCanceledException"/> before anything is read. Argument checks come first.</description></item>
/// </list>
/// </summary>
public interface IPoleOccupancyReader
{
    /// <summary>The occupancy of one pole, or null when the caller's organization has none on file for it.</summary>
    /// <exception cref="ArgumentException"><paramref name="poleFeatureId"/> is null or blank.</exception>
    Task<PoleOccupancy?> GetForPoleAsync(string poleFeatureId, CancellationToken ct);

    /// <summary>
    /// The occupancy of each of <paramref name="poleFeatureIds"/> that has one on file, in the
    /// ordinal order of the pole feature id. Poles with nothing on file are simply absent; null
    /// and repeated ids in the input are ignored; an empty input returns an empty list.
    ///
    /// Not paged: the result has at most one item per distinct id asked for, so the caller
    /// bounds it by what it passes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="poleFeatureIds"/> is null.</exception>
    Task<IReadOnlyList<PoleOccupancy>> GetForPolesAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct);

    /// <summary>
    /// Who must be notified about work on <paramref name="poleFeatureIds"/>: one
    /// <see cref="PoleOccupantPresence"/> per occupant organization holding an active contract
    /// (<see cref="PoleOccupancyEntry.IsContracted"/>: status
    /// <see cref="PoleAttachmentOccupantStatus.AuthorizedContracted"/> and a known occupant
    /// organization) on at least one of those poles, listing the poles where it does.
    ///
    /// <list type="bullet">
    ///   <item><description>Entries with no occupant organization, and entries in any other
    ///   status, never produce or extend a presence.</description></item>
    ///   <item><description>A contracted entry counts whether it sits on a regulated slot or
    ///   beyond them: an occupant on an over-capacity attachment is on the pole all the same.</description></item>
    ///   <item><description>Presences come in the ordinal order of the organization id's
    ///   canonical text (<c>Guid.ToString("D")</c>); each presence's poles are distinct and in
    ///   ordinal order. The pole owner itself is listed when it holds a contracted entry.</description></item>
    ///   <item><description>An empty input returns an empty list; null and repeated ids are ignored.</description></item>
    /// </list>
    ///
    /// Not paged: bounded by the poles asked for (at most one pole reference per distinct id,
    /// per organization on it).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="poleFeatureIds"/> is null.</exception>
    Task<IReadOnlyList<PoleOccupantPresence>> FindContractedOccupantsAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct);

    /// <summary>
    /// One page of the poles with occupancy on file, in the ordinal order of the pole feature id
    /// (unique within the organization, so the order is total), with
    /// <see cref="PagedResult{T}.TotalCount"/> counting every match. A page past the end has no
    /// items and the same total.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    Task<PagedResult<PoleOccupancy>> GetPageAsync(PoleOccupancyQuery query, CancellationToken ct);
}
