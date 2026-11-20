using GeoAssets.Core.Models;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>An <see cref="ITargetLedgerClient"/> that records its calls and fails the ones it is told to, for the host's ledger-facing services.</summary>
internal sealed class FakeTargetLedgerClient : ITargetLedgerClient
{
    public List<TargetLedgerReserveCommand> Previews { get; } = [];

    public List<TargetLedgerReserveCommand> Reservations { get; } = [];

    public List<(TargetLedgerKey Key, Guid OrganizationId, IReadOnlyCollection<string>? TargetIds, string? Actor)> Releases { get; } = [];

    public List<(int Skip, int Take)> UsagePages { get; } = [];

    /// <summary>Thrown by <see cref="ReserveAsync"/> and <see cref="PreviewAsync"/> when set.</summary>
    public Exception? ReserveFailure { get; set; }

    /// <summary>Thrown by <see cref="ReleaseAsync"/> when set.</summary>
    public Exception? ReleaseFailure { get; set; }

    /// <summary>The entries the ledger holds for the order, served page by page.</summary>
    public List<TargetLedgerEntry> Entries { get; } = [];

    public Task<TargetSelectionPreview> PreviewAsync(TargetLedgerReserveCommand command, CancellationToken ct)
    {
        Previews.Add(command);
        if (ReserveFailure is not null) return Task.FromException<TargetSelectionPreview>(ReserveFailure);

        return Task.FromResult(new TargetSelectionPreview
        {
            OrderTypeId = command.OrderTypeId,
            Matched = 0,
            Eligible = 0,
            AlreadyInOrder = 0,
            TruncatedByLimit = 0,
            WarningCount = 0,
            BlockedCount = 0,
            FirstPage = new PagedResult<ServiceOrderTarget> { Items = [], TotalCount = 0 },
        });
    }

    public Task<TargetSelectionResult> ReserveAsync(TargetLedgerReserveCommand command, CancellationToken ct)
    {
        Reservations.Add(command);
        if (ReserveFailure is not null) return Task.FromException<TargetSelectionResult>(ReserveFailure);

        return Task.FromResult(new TargetSelectionResult
        {
            OrderTypeId = command.OrderTypeId,
            Added = Entries.Count,
            Matched = Entries.Count,
            Eligible = Entries.Count,
            AlreadyInOrder = 0,
            TruncatedByLimit = 0,
            WarningCount = 0,
            BlockedCount = 0,
        });
    }

    public Task<IReadOnlyList<TargetLedgerEventItem>?> ReleaseAsync(
        TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, string? actor, CancellationToken ct)
    {
        Releases.Add((key, organizationId, targetIds, actor));
        return ReleaseFailure is not null
            ? Task.FromException<IReadOnlyList<TargetLedgerEventItem>?>(ReleaseFailure)
            : Task.FromResult<IReadOnlyList<TargetLedgerEventItem>?>([]);
    }

    public Task<IReadOnlyList<TargetLedgerEventItem>?> CommitAsync(
        TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, string? actor, CancellationToken ct) =>
        throw new NotSupportedException("The NeutralOperator host never commits through the ledger.");

    public Task<PagedResult<TargetLedgerEntry>> GetUsagesAsync(TargetLedgerKey key, Guid organizationId, int skip, int take, CancellationToken ct)
    {
        UsagePages.Add((skip, take));
        return Task.FromResult(new PagedResult<TargetLedgerEntry> { Items = [.. Entries.Skip(skip).Take(take)], TotalCount = Entries.Count });
    }
}
