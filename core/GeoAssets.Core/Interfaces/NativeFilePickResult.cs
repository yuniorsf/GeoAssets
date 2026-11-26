namespace GeoAssets.Core.Interfaces;

/// <summary>Result of a successful <see cref="INativeFilePicker.PickBinaryFileAsync"/> call.</summary>
public sealed record NativeFilePickResult(byte[] Content, string FileName, string FullPath);
