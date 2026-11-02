using System.Globalization;
using System.Text;

namespace GeoAssets.Core.Isp.Compliance.PoleAudit;

/// <summary>
/// Compiles one or more poles' <see cref="PoleDefenseBundle"/>s (XD01-285) into the certified
/// legal defense kit XD01-286's AC asks for: a single document citing the statutory basis, with
/// every required element present or explicitly flagged as missing (AC1/AC3), and every
/// underlying evidence/license/dossier file carried along as an attachment the caller can bundle
/// (e.g. zip) alongside the document.
///
/// No PDF/document-generation library is a dependency of this project yet, and the AC only asks
/// for "an exportable document", not a specific file format — so the document itself is plain
/// text, kept deterministic and easy to test; the host layer decides how to package it (e.g.
/// GeoAssets.FieldService.Server zips it with the attachments).
/// </summary>
public static class PoleLegalDefenseDossierGenerator
{
    private const string StatutoryBasis = "Decreto 12.068/2024; Resolução Conjunta ANEEL/ANATEL nº 4/2014";

    /// <param name="bundles">One per pole that resolved to a real feature.</param>
    /// <param name="notFoundPoleFeatureIds">
    /// Requested pole ids that did not resolve to a real feature at all — flagged the same way as
    /// any other missing evidence (AC3) rather than silently dropped from the batch.
    /// </param>
    public static PoleLegalDefenseDossierResult Generate(
        IReadOnlyList<PoleDefenseBundle> bundles, IReadOnlyList<string> notFoundPoleFeatureIds, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        ArgumentNullException.ThrowIfNull(notFoundPoleFeatureIds);
        if (bundles.Count == 0 && notFoundPoleFeatureIds.Count == 0)
            throw new ArgumentException("At least one pole must be requested.", nameof(bundles));

        var missingItems = new List<PoleLegalDefenseMissingItem>();
        var attachments = new List<ConcessionaireDossierFile>();
        var document = new StringBuilder();

        AppendHeader(document, bundles.Count + notFoundPoleFeatureIds.Count, generatedAtUtc);

        foreach (var poleId in notFoundPoleFeatureIds)
        {
            document.AppendLine(CultureInfo.InvariantCulture, $"--- Pole {poleId} ---");
            const string description = "Pole feature id does not resolve to a known feature.";
            document.AppendLine("MISSING: " + description);
            document.AppendLine();
            missingItems.Add(new PoleLegalDefenseMissingItem(poleId, description));
        }

        foreach (var bundle in bundles)
            AppendPoleSection(document, bundle, generatedAtUtc, missingItems, attachments);

        return new PoleLegalDefenseDossierResult(document.ToString(), missingItems, attachments);
    }

    private static void AppendHeader(StringBuilder document, int poleCount, DateTimeOffset generatedAtUtc)
    {
        document.AppendLine("EMERGENCY LEGAL DEFENSE DOSSIER");
        document.AppendLine(CultureInfo.InvariantCulture, $"Generated: {generatedAtUtc:O}");
        document.AppendLine(CultureInfo.InvariantCulture, $"Statutory basis: {StatutoryBasis}");
        document.AppendLine(CultureInfo.InvariantCulture, $"Poles covered: {poleCount}");
        document.AppendLine();
    }

    private static void AppendPoleSection(
        StringBuilder document, PoleDefenseBundle bundle, DateTimeOffset generatedAtUtc,
        List<PoleLegalDefenseMissingItem> missingItems, List<ConcessionaireDossierFile> attachments)
    {
        document.AppendLine(CultureInfo.InvariantCulture, $"--- Pole {bundle.PoleFeatureId} ---");

        AppendEvidenceSection(document, bundle, missingItems, attachments);
        AppendLicenseSection(document, bundle, generatedAtUtc, missingItems, attachments);
        AppendDossierSection(document, bundle, attachments);

        document.AppendLine();
    }

    private static void AppendEvidenceSection(
        StringBuilder document, PoleDefenseBundle bundle, List<PoleLegalDefenseMissingItem> missingItems,
        List<ConcessionaireDossierFile> attachments)
    {
        var index = 0;
        foreach (var kind in Enum.GetValues<PoleAuditEvidenceKind>())
        {
            var matches = bundle.Evidence.Where(e => e.Kind == kind).ToList();
            if (matches.Count == 0)
            {
                var description = $"No sealed {kind} evidence on file for this pole (or it was captured before the valid-installation timestamp).";
                document.AppendLine("MISSING: " + description);
                missingItems.Add(new PoleLegalDefenseMissingItem(bundle.PoleFeatureId, description));
                continue;
            }

            foreach (var entry in matches)
            {
                document.AppendLine(CultureInfo.InvariantCulture,
                    $"Evidence: {entry.Kind} captured {entry.CapturedAtUtc:O} at ({entry.Latitude}, {entry.Longitude})");
                attachments.Add(new ConcessionaireDossierFile(
                    $"{bundle.PoleFeatureId}/evidence-{entry.Kind}-{index}.jpg", entry.SignedJpegBytes));
                index++;
            }
        }
    }

    private static void AppendLicenseSection(
        StringBuilder document, PoleDefenseBundle bundle, DateTimeOffset generatedAtUtc,
        List<PoleLegalDefenseMissingItem> missingItems, List<ConcessionaireDossierFile> attachments)
    {
        if (bundle.LicenseRecords.Count == 0)
        {
            const string description = "No SCM license record on file for this pole.";
            document.AppendLine("MISSING: " + description);
            missingItems.Add(new PoleLegalDefenseMissingItem(bundle.PoleFeatureId, description));
            return;
        }

        var activeLicense = bundle.LicenseRecords.FirstOrDefault(l => l.ExpiresAtUtc is null || l.ExpiresAtUtc >= generatedAtUtc);
        if (activeLicense is null)
        {
            const string description = "SCM license record(s) on file, but none are active as of dossier generation time (all expired).";
            document.AppendLine("MISSING: " + description);
            missingItems.Add(new PoleLegalDefenseMissingItem(bundle.PoleFeatureId, description));
        }

        for (var i = 0; i < bundle.LicenseRecords.Count; i++)
        {
            var license = bundle.LicenseRecords[i];
            document.AppendLine(CultureInfo.InvariantCulture,
                $"License: {license.ScmLicenseNumber} ({license.LicenseHolderName}), issued {license.IssuedAtUtc:O}, expires {license.ExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "never"}");
            var licenseText = $"ScmLicenseNumber: {license.ScmLicenseNumber}\nLicenseHolderName: {license.LicenseHolderName}\nIssuedAtUtc: {license.IssuedAtUtc:O}\nExpiresAtUtc: {license.ExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "never"}\n";
            attachments.Add(new ConcessionaireDossierFile($"{bundle.PoleFeatureId}/license-{i}.txt", Encoding.UTF8.GetBytes(licenseText)));
        }
    }

    private static void AppendDossierSection(StringBuilder document, PoleDefenseBundle bundle, List<ConcessionaireDossierFile> attachments)
    {
        if (bundle.DossierRecords.Count == 0)
        {
            document.AppendLine("Compliance dossier: none generated for this pole.");
            return;
        }

        foreach (var dossierRecord in bundle.DossierRecords)
        {
            document.AppendLine(CultureInfo.InvariantCulture,
                $"Compliance dossier: {dossierRecord.DistributorName}, generated {dossierRecord.GeneratedAtUtc:O}");
            foreach (var file in dossierRecord.Dossier.Files)
                attachments.Add(new ConcessionaireDossierFile($"{bundle.PoleFeatureId}/dossier/{file.FileName}", file.Content));
        }
    }
}
