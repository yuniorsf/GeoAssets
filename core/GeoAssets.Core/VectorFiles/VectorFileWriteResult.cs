namespace GeoAssets.Core.VectorFiles;

/// <summary>What an <see cref="IVectorFileWriter"/> wrote (XD01-466).</summary>
/// <param name="FeaturesWritten">How many features the file holds.</param>
/// <param name="Attributes">The attribute columns written after <c>id</c> and <c>name</c>, in order.</param>
public sealed record VectorFileWriteResult(long FeaturesWritten, IReadOnlyList<string> Attributes);
