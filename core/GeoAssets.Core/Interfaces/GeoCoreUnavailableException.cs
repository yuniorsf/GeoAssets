namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Thrown by a vertical host's adapter over <see cref="IGeoCoreClient"/> when the Core GeoAssets
/// Server could not answer a read (XD01-386): a transport failure, a non-success status, or a
/// timeout that is not the caller's own cancellation. Typed so a caller can tell "the core is
/// unreachable" apart from "the asset does not exist" — a guard fails closed on it (the request is
/// refused, never silently allowed) and a host maps it to a <c>503</c> problem with code
/// <see cref="ProblemCode"/>.
/// </summary>
public sealed class GeoCoreUnavailableException : Exception
{
    /// <summary>Problem <c>type</c>/code a host reports this exception under.</summary>
    public const string ProblemCode = "geocore.unavailable";

    public GeoCoreUnavailableException()
        : base("The Core GeoAssets Server is unavailable.")
    {
    }

    public GeoCoreUnavailableException(string message) : base(message)
    {
    }

    public GeoCoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
