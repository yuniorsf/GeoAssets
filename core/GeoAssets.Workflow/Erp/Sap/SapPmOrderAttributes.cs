namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>
/// Well-known <see cref="Orders.IServiceOrder.Attributes"/> keys a SAP PM-spawned order carries,
/// same "plain string bag, no dedicated columns" convention as <see cref="Isp.Bss.BssOrderAttributes"/>.
/// </summary>
public static class SapPmOrderAttributes
{
    public const string FunctionalLocationKey = "sapFunctionalLocation";
    public const string OrderNumberKey = "sapOrderNumber";
    public const string NotificationIdKey = "sapNotificationId";
    public const string EvidenceDossierUrlKey = "sapEvidenceDossierUrl";

    /// <summary>
    /// Stamped by <see cref="SapPmOrderConfirmationDispatchService"/> right after a successful
    /// outbound dispatch — its mere presence is this ticket's "exactly one outbound confirmation
    /// call" guard: a retried transition call sees it already set and skips re-dispatching,
    /// same "well-known attribute as a done-marker" convention <see cref="SlaGuard.SlaGuardEngine.DeadlineAttributeKey"/> already uses for a different purpose.
    /// </summary>
    public const string ConfirmationDispatchedAtKey = "sapConfirmationDispatchedAtUtc";
}
