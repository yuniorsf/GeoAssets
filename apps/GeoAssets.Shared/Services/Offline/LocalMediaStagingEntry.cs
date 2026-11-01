namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// A locally-captured evidence photo staged on-device, awaiting upload once connectivity
/// resumes (XD01-243). Owned separately from <see cref="LocalOutboxEntry"/> since media bytes
/// are large and shouldn't round-trip through the same small JSON-mutation queue — the hardware
/// capture pipeline (XD01-244) and photo-gating engine (XD01-246) are the actual producers/
/// consumers of this store; this ticket only provisions it.
/// </summary>
public sealed record LocalMediaStagingEntry(
    Guid Id,
    string ServiceOrderId,
    string LocalFilePath,
    DateTime CapturedAtUtc,
    int UploadAttemptCount,
    bool Uploaded);
