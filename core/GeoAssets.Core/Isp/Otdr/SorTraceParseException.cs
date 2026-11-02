namespace GeoAssets.Core.Isp.Otdr;

/// <summary>
/// Thrown by <see cref="SorTraceParser.Parse"/> when a <c>.SOR</c> trace is malformed,
/// truncated, or structurally inconsistent (XD01-283's AC: "rejected with a diagnostic, not a
/// crash") — never let a raw <see cref="IndexOutOfRangeException"/>/<see cref="OverflowException"/>
/// escape from the parser itself.
/// </summary>
public sealed class SorTraceParseException : Exception
{
    public SorTraceParseException()
    {
    }

    public SorTraceParseException(string message) : base(message)
    {
    }

    public SorTraceParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
