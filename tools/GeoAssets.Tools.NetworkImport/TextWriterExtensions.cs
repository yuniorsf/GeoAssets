namespace GeoAssets.Tools.NetworkImport;

/// <summary>Line output through the run's writers, honouring cancellation.</summary>
internal static class TextWriterExtensions
{
    public static Task LineAsync(this TextWriter writer, string text, CancellationToken cancellationToken) =>
        writer.WriteLineAsync(text.AsMemory(), cancellationToken);
}
