namespace GeoAssets.Core.Interfaces;

/// <summary>What <see cref="INativeFilePicker.PickBinaryFileAsync"/> answers about the picked file (XD01-438).</summary>
public enum NativeFilePickContent
{
    /// <summary>The file's bytes (<see cref="NativeFilePickResult.Content"/>), read whole, with its name and full path.</summary>
    Bytes = 0,

    /// <summary>
    /// Only its name and full path — <see cref="NativeFilePickResult.Content"/> is empty — for a file too large to read
    /// into memory (an offline <c>.pmtiles</c> map pack), which the caller then reads or copies from the path.
    /// </summary>
    PathOnly = 1,
}
