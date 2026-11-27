namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Result of a successful <see cref="INativeFilePicker.PickBinaryFileAsync"/> call. <see cref="Content"/> is empty when
/// the call asked for <see cref="NativeFilePickContent.PathOnly"/>.
/// </summary>
public sealed record NativeFilePickResult(byte[] Content, string FileName, string FullPath);
