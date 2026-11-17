namespace GeoAssets.Tools.SharedUseImport;

/// <summary>What the import did with one planned pole.</summary>
internal enum SharedUsePoleOutcome
{
    /// <summary>The pole had nothing on file; its planned occupancy was written.</summary>
    Created,

    /// <summary>The pole already holds exactly the planned occupancy (an earlier run wrote it); nothing was written.</summary>
    Unchanged,

    /// <summary>Dry run: the pole has nothing on file and would be written.</summary>
    WouldCreate,

    /// <summary>The pole already holds a different occupancy; the import never overwrites it.</summary>
    ExistingOccupancyDiffers,

    /// <summary>Another writer put the pole on file between the read and the write; nothing was written.</summary>
    ConcurrentWrite,
}
