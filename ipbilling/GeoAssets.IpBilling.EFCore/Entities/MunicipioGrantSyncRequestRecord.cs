using GeoAssets.IpBilling.GrantSync;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>
/// EF entity for the <c>MunicipioGrantSyncRequests</c> outbox (XD01-395): one row per grant change a município save
/// queued, written in the save's own transaction and sent to the kernel by the IpBilling host's dispatcher. No foreign key
/// to <c>Municipios</c>: a deleted município's revoke must outlive its row. Times are UTC.
/// </summary>
internal sealed class MunicipioGrantSyncRequestRecord
{
    /// <summary>Database-generated; orders the requests of a município (oldest first).</summary>
    public long Id { get; set; }

    public Guid MunicipioId { get; set; }

    public MunicipioGrantSyncKind Kind { get; set; }

    public string IbgeCode { get; set; } = string.Empty;

    public Guid DistribuidoraOrganizationId { get; set; }

    public Guid? PrefeituraOrganizationId { get; set; }

    public Guid? PreviousPrefeituraOrganizationId { get; set; }

    public string? PreviousIbgeCode { get; set; }

    public Guid? PreviousDistribuidoraOrganizationId { get; set; }

    public string RequestedBy { get; set; } = string.Empty;

    public DateTime RequestedAt { get; set; }

    /// <summary>Failed attempts so far.</summary>
    public int Attempts { get; set; }

    public DateTime NextAttemptAt { get; set; }

    /// <summary>The dispatcher worker holding the request, until <see cref="LeaseExpiresAt"/>.</summary>
    public string? LeasedBy { get; set; }

    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>The last failed attempt's error, bounded; never a token or secret.</summary>
    public string? LastError { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Set when the request ran out of attempts; the row stays for operators to see.</summary>
    public DateTime? FailedAt { get; set; }
}
