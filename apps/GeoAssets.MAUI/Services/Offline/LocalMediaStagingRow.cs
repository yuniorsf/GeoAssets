using SQLite;

namespace GeoAssets.MAUI.Services.Offline;

[Table("local_media_staging")]
internal sealed class LocalMediaStagingRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string ServiceOrderId { get; set; } = string.Empty;
    public string LocalFilePath { get; set; } = string.Empty;
    public DateTime CapturedAtUtc { get; set; }
    public int UploadAttemptCount { get; set; }
    public bool Uploaded { get; set; }
}
