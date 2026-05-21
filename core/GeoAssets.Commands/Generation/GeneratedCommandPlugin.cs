namespace GeoAssets.Commands.Generation;

public sealed record GeneratedCommandPlugin(
    string AssemblyName,
    string Namespace,
    string ProjectDirectory,
    IReadOnlyList<GeneratedPluginFile> Files);
