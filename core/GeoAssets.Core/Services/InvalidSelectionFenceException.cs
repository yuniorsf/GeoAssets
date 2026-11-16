namespace GeoAssets.Core.Services;

/// <summary>
/// Thrown by <see cref="SelectionFenceValidator.Validate"/> when a selection fence is rejected.
/// <see cref="Defect"/> carries the reason as a stable, typed value; the fence is never repaired.
/// </summary>
public sealed class InvalidSelectionFenceException : Exception
{
    public SelectionFenceDefect Defect { get; }

    public InvalidSelectionFenceException(SelectionFenceDefect defect)
        : base($"The selection fence is invalid: {defect}.")
    {
        Defect = defect;
    }

    public InvalidSelectionFenceException(SelectionFenceDefect defect, string message)
        : base(message)
    {
        Defect = defect;
    }

    public InvalidSelectionFenceException()
    {
    }

    public InvalidSelectionFenceException(string message) : base(message)
    {
    }

    public InvalidSelectionFenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
