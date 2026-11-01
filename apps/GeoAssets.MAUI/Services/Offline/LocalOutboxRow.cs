using SQLite;

namespace GeoAssets.MAUI.Services.Offline;

[Table("local_action_outbox")]
internal sealed class LocalOutboxRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public long SequenceNumber { get; set; }

    public string ServiceOrderId { get; set; } = string.Empty;
    public string ServiceOrderJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    [Indexed]
    public bool Dispatched { get; set; }
}
