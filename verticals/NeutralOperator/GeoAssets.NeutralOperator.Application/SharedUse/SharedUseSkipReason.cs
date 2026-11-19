namespace GeoAssets.NeutralOperator.Application.SharedUse;

/// <summary>Why an occupancy entry on one of the order's poles gets no dispatch.</summary>
public enum SharedUseSkipReason
{
    /// <summary>
    /// The entry is not under an active contract: its status is anything but
    /// <c>AuthorizedContracted</c> (a reservation, an abandoned cable, a clandestine attachment).
    /// </summary>
    NoActiveContract = 0,

    /// <summary>The entry is contracted but names no organization, so there is nobody to dispatch to.</summary>
    UnknownOrganization = 1,

    /// <summary>The entry belongs to the organization that owns the pole and the order itself.</summary>
    PoleOwner = 2,
}
