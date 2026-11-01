using System.Globalization;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services.Scanning;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>
/// Assembles the FTTH drop-install workflow state machine (<see cref="FtthDropInstallTransitioner"/>,
/// XD01-273), OPM budget verification (<see cref="OpticalBudgetVerificationService"/>, XD01-274),
/// barcode/QR scan validation (<see cref="FieldWorkBarcodeScanValidator"/>, XD01-275), and the
/// mandatory photo-evidence slots (<see cref="FtthEvidenceCompletenessChecker"/>, XD01-278) into
/// the complete technician-facing checklist (XD01-277): CTO arrival → OPM (CTO) reading → drop
/// launch/tag scan → OPM (PTO) reading + budget verify → ONT scan → photo capture → complete.
///
/// Lives in <c>GeoAssets.Shared</c> rather than <c>GeoAssets.Workflow</c> because
/// <see cref="FieldWorkBarcodeScanValidator"/> lives here too (dependency direction: Shared →
/// Workflow/Core, never the reverse) — this is the one layer that can see every piece.
///
/// Pure domain orchestration: every method mutates and returns via the passed-in
/// <see cref="ServiceOrder"/>, with no SQLite/HTTP/camera I/O of its own. A MAUI host calls the
/// camera/OPM-meter/barcode-scanner hardware itself, then hands this service the resulting
/// values; persisting the mutated order (<c>ILocalServiceOrderStore</c>) and queuing it for sync
/// (<c>ILocalActionOutbox</c>) is the caller's job too — the same "pure core, I/O at the edges"
/// split every other FieldWork service in this session already follows.
/// </summary>
public sealed class FtthDropInstallChecklistService(OrderType ftthOrderType, OrderType ctoRepairOrderType)
{
    /// <summary>CRM sale → CTO Port Test: the crew has arrived at the (already-assigned) CTO.</summary>
    public FtthChecklistStepResult RecordCtoArrival(ServiceOrder order, TimeProvider timeProvider)
    {
        FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.CtoPortTest, timeProvider, ftthOrderType);
        return FtthChecklistStepResult.Success();
    }

    /// <summary>CTO Port Test → Drop Launch: records the CTO's OPM reading. Not yet verified against the budget — that happens once the PTO reading is also in hand.</summary>
    public FtthChecklistStepResult RecordCtoReading(ServiceOrder order, double ctoReadingDbm, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(order);

        order.Attributes[FtthDropInstallChecklistAttributes.CtoReadingDbmKey] = ctoReadingDbm.ToString(CultureInfo.InvariantCulture);
        FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.DropLaunch, timeProvider, ftthOrderType);
        return FtthChecklistStepResult.Success();
    }

    /// <summary>Drop Launch → Tag Affixed: scans the yellow identification tag.</summary>
    public FtthChecklistStepResult RecordTagScan(ServiceOrder order, string rawScannedPayload, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(order);

        var scan = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.YellowTagId, rawScannedPayload);
        if (!scan.IsValid)
            return FtthChecklistStepResult.Failure(scan.RejectionReason!);

        order.Attributes[FtthDropInstallChecklistAttributes.YellowTagIdKey] = scan.NormalizedValue!;
        FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.TagAffixed, timeProvider, ftthOrderType);
        return FtthChecklistStepResult.Success();
    }

    /// <summary>
    /// Tag Affixed → PTO Power: records the PTO's OPM reading and verifies it against the
    /// optical budget together with the CTO reading recorded earlier. On failure, the order is
    /// left in <see cref="FtthDropInstallStatus.TagAffixed"/> — the AC's "blocks progression...
    /// with an on-screen explanation" — and, if the CTO reading itself was below budget, builds
    /// (but does not persist) the upstream repair order.
    /// </summary>
    public FtthChecklistStepResult RecordPtoReadingAndVerifyBudget(ServiceOrder order, double ptoReadingDbm, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (!order.Attributes.TryGetValue(FtthDropInstallChecklistAttributes.CtoReadingDbmKey, out var ctoRaw)
            || !double.TryParse(ctoRaw, CultureInfo.InvariantCulture, out var ctoReadingDbm))
            return FtthChecklistStepResult.Failure("CTO reading was never recorded — return to the CTO Port Test step.");

        order.Attributes[FtthDropInstallChecklistAttributes.PtoReadingDbmKey] = ptoReadingDbm.ToString(CultureInfo.InvariantCulture);

        var verification = OpticalBudgetVerificationService.Verify(ctoReadingDbm, ptoReadingDbm);
        if (!verification.Passed)
        {
            order.Attributes[FtthDropInstallTransitioner.OpmReadingPassedAttributeKey] = "false";

            ServiceOrder? repairOrder = null;
            if (verification.RequiresUpstreamCtoRepair)
            {
                var ctoFeatureId = order.Attributes.GetValueOrDefault(FtthDropInstallChecklistAttributes.CtoFeatureIdKey, string.Empty);
                repairOrder = UpstreamCtoRepairOrderSpawner.BuildIfNeeded(verification, order, ctoFeatureId, ctoRepairOrderType, timeProvider);
            }

            return new FtthChecklistStepResult(false, verification.ViolationDetail, repairOrder);
        }

        order.Attributes[FtthDropInstallTransitioner.OpmReadingPassedAttributeKey] = "true";
        FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.PtoPower, timeProvider, ftthOrderType);
        return FtthChecklistStepResult.Success();
    }

    /// <summary>PTO Power → ONT Provisioned: scans the ONT's FSAN and MAC address labels.</summary>
    public FtthChecklistStepResult RecordOntScan(ServiceOrder order, string fsanRawPayload, string macRawPayload, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(order);

        var fsanScan = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.OntFsan, fsanRawPayload);
        if (!fsanScan.IsValid)
            return FtthChecklistStepResult.Failure(fsanScan.RejectionReason!);

        var macScan = FieldWorkBarcodeScanValidator.Validate(ExpectedScanField.OntMacAddress, macRawPayload);
        if (!macScan.IsValid)
            return FtthChecklistStepResult.Failure(macScan.RejectionReason!);

        order.Attributes[FtthDropInstallChecklistAttributes.OntFsanKey] = fsanScan.NormalizedValue!;
        order.Attributes[FtthDropInstallChecklistAttributes.OntMacAddressKey] = macScan.NormalizedValue!;
        FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.OntProvisioned, timeProvider, ftthOrderType);
        return FtthChecklistStepResult.Success();
    }

    /// <summary>
    /// Records one of the four mandatory evidence photo slots as captured. Idempotent. Static —
    /// unlike every other step here, it needs neither <paramref name="order"/>'s workflow graph
    /// nor the repair-order type this instance carries.
    /// </summary>
    public static FtthChecklistStepResult RecordEvidencePhoto(ServiceOrder order, FtthEvidenceSlot slot)
    {
        ServiceOrderFtthEvidenceAttributes.RecordCapturedSlot(order, slot);
        return FtthChecklistStepResult.Success();
    }

    /// <summary>
    /// ONT Provisioned → Activated: completes the checklist once all four mandatory evidence
    /// photo slots are filled (XD01-278's AC, enforced here as this ticket's own completion
    /// gate). On success, the order carries every field the checklist collected — CTO/PTO
    /// readings, the OPM verdict, the yellow tag ID, the ONT FSAN/MAC, and all four evidence
    /// slots — for whatever BSS activation sync eventually reads it.
    /// </summary>
    public FtthChecklistStepResult TryComplete(ServiceOrder order, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(order);

        var missing = FtthEvidenceCompletenessChecker.MissingSlots(order.Attributes);
        if (missing.Count > 0)
            return FtthChecklistStepResult.Failure($"Missing required evidence photo(s): {string.Join(", ", missing)}.");

        FtthDropInstallTransitioner.Transition(order, FtthDropInstallStatus.Activated, timeProvider, ftthOrderType);
        return FtthChecklistStepResult.Success();
    }
}
