using System.Data.Common;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// The transaction a <see cref="NeutralOperatorDbContext"/> joins before each use (XD01-380). Until Phase 7 the vertical's
/// tables share the kernel's connection, and whatever the kernel's unit of work, order repository or outbox has open on it
/// — an order transition, an occupancy approval, a shared-use dispatch — the vertical's writes must be part of.
/// </summary>
public interface IAmbientDbTransaction
{
    /// <summary>The transaction open on the shared connection right now, or null when there is none.</summary>
    DbTransaction? Current { get; }
}
