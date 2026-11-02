namespace GeoAssets.Core.Isp.NeutralOperator;

/// <summary>Thrown by <see cref="PoleAttachmentSlotMatrix"/> when a slot status change isn't
/// in <see cref="PoleAttachmentSlotTransitions"/>'s allowed set.</summary>
public sealed class InvalidPoleAttachmentSlotTransitionException : InvalidOperationException
{
    public PoleAttachmentOccupantStatus From { get; }
    public PoleAttachmentOccupantStatus To { get; }

    public InvalidPoleAttachmentSlotTransitionException(PoleAttachmentOccupantStatus from, PoleAttachmentOccupantStatus to)
        : base($"Cannot transition a pole attachment slot from '{from}' to '{to}'.")
    {
        From = from;
        To = to;
    }

    public InvalidPoleAttachmentSlotTransitionException()
    {
    }

    public InvalidPoleAttachmentSlotTransitionException(string message) : base(message)
    {
    }

    public InvalidPoleAttachmentSlotTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
