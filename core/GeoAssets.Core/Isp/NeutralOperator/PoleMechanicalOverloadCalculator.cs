namespace GeoAssets.Core.Isp.NeutralOperator;

/// <summary>
/// Sums the declared mechanical load (<see cref="PoleAttachmentSlot.CableLoadDaN"/>) of a pole's
/// <see cref="PoleAttachmentOccupantStatus.AuthorizedContracted"/> attachments and compares it
/// against the pole's rated daN (traction/load) capacity per ABNT NBR 15214/NBR 15688, as named by
/// XD01-262's AC. Deliberately scoped to this single summation — not a structural-engineering
/// simulation (wind load, sag, span geometry, etc.), matching the ticket's own "flagging/reporting
/// only" scope. Attachments that are not <c>AuthorizedContracted</c> (reservations, abandoned
/// dead-drops, clandestine cables — XD01-263's own detection target) are excluded, since an
/// unauthorized cable's undeclared load isn't something this calculation can responsibly assert.
/// </summary>
public static class PoleMechanicalOverloadCalculator
{
    /// <summary>The <c>geo_entity.CustomAttributes</c> key a caller sets to surface the
    /// reviewable alert XD01-262's AC asks for.</summary>
    public const string FlaggedAttributeKey = "mechanicalOverloadFlagged";

    public static PoleMechanicalOverloadResult Evaluate(PoleAttachmentSlotMatrix matrix, decimal ratedCapacityDaN)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        var totalLoad = matrix.Slots
            .Where(slot => slot.Status == PoleAttachmentOccupantStatus.AuthorizedContracted)
            .Sum(slot => slot.CableLoadDaN);

        return new PoleMechanicalOverloadResult(totalLoad > ratedCapacityDaN, totalLoad, ratedCapacityDaN);
    }
}
