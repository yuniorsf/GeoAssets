using System.Data.Common;

namespace GeoAssets.FieldService.Persistence;

/// <summary>
/// The transaction a <see cref="FieldServiceDbContext"/> joins before each use (XD01-381). Until Phase 7 the vertical's
/// tables share the kernel's connection, and whatever the kernel's unit of work, order repository or outbox has open on it
/// — the inspection commit's order transition, paving child order and outbox message above all — the vertical's writes
/// must be part of. The FieldService twin of the NeutralOperator vertical's port (XD01-380): verticals do not reference
/// each other's persistence.
/// </summary>
public interface IAmbientDbTransaction
{
    /// <summary>The transaction open on the shared connection right now, or null when there is none.</summary>
    DbTransaction? Current { get; }
}
