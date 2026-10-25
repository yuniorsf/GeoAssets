namespace GeoAssets.Core.Interfaces;

/// <summary>Result of a successful <see cref="INativeFilePicker.PickZipArchiveAsync"/> call.</summary>
public sealed record NativeFilePickResult(byte[] Content, string FileName, string FullPath);
