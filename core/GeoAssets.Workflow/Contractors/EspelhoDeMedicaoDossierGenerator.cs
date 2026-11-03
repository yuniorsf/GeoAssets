using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Compiles one inspected service order's <see cref="ContractorUsPosting"/>s (XD01-248's
/// immutable ledger) and <see cref="ContractorUsLedgerEntry"/> photo evidence into the signed
/// Espelho de Medição dossier (XD01-249).
///
/// Deliberately a pure function over already-resolved domain objects — no repository/DI
/// dependency here at all, so idempotency (AC3: re-generating for the same order yields an
/// identical signed artifact) is structural, not something to separately test for: the same
/// inputs always produce the same <see cref="EspelhoDeMedicaoDossierResult.DocumentText"/> and
/// <see cref="EspelhoDeMedicaoDossierResult.SignatureBase64"/>, since nothing here reads the
/// wall clock. The caller (a REST host) resolves the <see cref="IServiceOrder"/>, contractor
/// organization name, postings, and ledger entries first.
///
/// <strong>Tax retentions are not modeled as a business rule</strong> — real Brazilian
/// service-invoice withholding (ISS/INSS/PIS/COFINS/CSLL) has municipality- and regime-dependent
/// rates with no source of truth anywhere in this repository. <paramref name="retentionRate"/>
/// is a single caller-supplied aggregate percentage instead of a computed one; 0 is a valid,
/// explicit "no retention" choice, not a missing default.
/// </summary>
public static class EspelhoDeMedicaoDossierGenerator
{
    /// <param name="contractorName">
    /// A resolved display name for <paramref name="order"/>'s <c>OrganizationId</c>, if the
    /// caller has one — no host in this vertical currently carries Identity-DB access to
    /// resolve it itself (<c>GeoAssets.FieldService.Server</c> only has <c>ITenantContext</c>'s
    /// bare <c>Guid</c>), so this falls back to the id's own string form when omitted rather
    /// than requiring a lookup this layer has no way to perform.
    /// </param>
    public static EspelhoDeMedicaoDossierResult Generate(
        IServiceOrder order,
        string? contractorName,
        IReadOnlyList<ContractorUsPosting> postings,
        IReadOnlyList<ContractorUsLedgerEntry> ledgerEntries,
        decimal retentionRate,
        string tenantSecret)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(postings);
        ArgumentNullException.ThrowIfNull(ledgerEntries);
        ArgumentException.ThrowIfNullOrEmpty(tenantSecret);

        var resolvedContractorName = string.IsNullOrWhiteSpace(contractorName)
            ? order.OrganizationId.ToString()
            : contractorName;

        var items = BuildItemLines(postings, ledgerEntries);
        var totalBrl = Round(items.Sum(i => i.TotalBrl));
        var retentionBrl = Round(totalBrl * retentionRate);
        var netPayableBrl = Round(totalBrl - retentionBrl);
        var aprChecklistSignedAtUtc = order.Attributes.GetValueOrDefault("aprSignedAtUtc");

        var documentText = BuildDocumentText(
            order, resolvedContractorName, items, totalBrl, retentionRate, retentionBrl, netPayableBrl,
            aprChecklistSignedAtUtc);
        var signature = ComputeDossierSignature(documentText, tenantSecret);

        return new EspelhoDeMedicaoDossierResult(
            order.Id,
            order.OrganizationId,
            resolvedContractorName,
            order.Title,
            order.CreatedAt,
            order.CompletedAt,
            items,
            totalBrl,
            retentionRate,
            retentionBrl,
            netPayableBrl,
            aprChecklistSignedAtUtc,
            documentText,
            signature);
    }

    private static List<EspelhoDeMedicaoItemLine> BuildItemLines(
        IReadOnlyList<ContractorUsPosting> postings, IReadOnlyList<ContractorUsLedgerEntry> ledgerEntries) =>
        postings
            .OrderBy(p => p.ItemCode, StringComparer.Ordinal)
            .ThenBy(p => p.Id)
            .Select(posting => BuildItemLine(posting, ledgerEntries))
            .ToList();

    private static EspelhoDeMedicaoItemLine BuildItemLine(
        ContractorUsPosting posting, IReadOnlyList<ContractorUsLedgerEntry> ledgerEntries)
    {
        var hashes = ledgerEntries
            .Where(e => string.Equals(e.UsCode, posting.ItemCode, StringComparison.Ordinal))
            .SelectMany(e => e.PhotoProof)
            .Select(ComputePhotoProofHash)
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToList();

        return new EspelhoDeMedicaoItemLine(
            posting.ItemCode,
            posting.Quantity,
            posting.UnitUs,
            Round(posting.TotalUs),
            posting.MonetaryRate,
            Round(posting.TotalBrl),
            hashes);
    }

    /// <summary>
    /// SHA-256 over the photo's own canonical, stored fields (URL + geolocation + capture time) —
    /// not a re-hash of fetched image bytes. <see cref="LedgerPhotoProof"/> only persists a URL,
    /// never the raw image content, so re-fetching and hashing bytes isn't available to a pure
    /// domain-layer function; this still ties the dossier to a tamper-evident content fingerprint
    /// of exactly what was claimed as evidence.
    /// </summary>
    private static string ComputePhotoProofHash(LedgerPhotoProof photo)
    {
        var canonical = string.Join('|',
            photo.Url,
            photo.Location.Longitude.ToString("R", CultureInfo.InvariantCulture),
            photo.Location.Latitude.ToString("R", CultureInfo.InvariantCulture),
            photo.CapturedAt.ToString("O", CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }

    private static string BuildDocumentText(
        IServiceOrder order,
        string contractorName,
        IReadOnlyList<EspelhoDeMedicaoItemLine> items,
        decimal totalBrl,
        decimal retentionRate,
        decimal retentionBrl,
        decimal netPayableBrl,
        string? aprChecklistSignedAtUtc)
    {
        var document = new StringBuilder();
        document.AppendLine("ESPELHO DE MEDIÇÃO");
        document.AppendLine(CultureInfo.InvariantCulture, $"Service Order: {order.Id} — {order.Title}");
        document.AppendLine(CultureInfo.InvariantCulture, $"Contractor: {contractorName} ({order.OrganizationId})");
        document.AppendLine(CultureInfo.InvariantCulture, $"Execution started: {order.CreatedAt:O}");
        document.AppendLine(CultureInfo.InvariantCulture,
            $"Execution completed: {order.CompletedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "(not yet completed)"}");
        document.AppendLine(CultureInfo.InvariantCulture,
            $"APR safety checklist: {aprChecklistSignedAtUtc ?? "MISSING: no digital APR checklist on file for this order."}");
        document.AppendLine();

        foreach (var item in items)
            AppendItemSection(document, item);

        document.AppendLine(CultureInfo.InvariantCulture, $"Total (BRL): {totalBrl.ToString("F2", CultureInfo.InvariantCulture)}");
        document.AppendLine(CultureInfo.InvariantCulture,
            $"Retention ({retentionRate.ToString("P2", CultureInfo.InvariantCulture)}): {retentionBrl.ToString("F2", CultureInfo.InvariantCulture)}");
        document.AppendLine(CultureInfo.InvariantCulture, $"Net payable (BRL): {netPayableBrl.ToString("F2", CultureInfo.InvariantCulture)}");

        return document.ToString();
    }

    private static void AppendItemSection(StringBuilder document, EspelhoDeMedicaoItemLine item)
    {
        document.AppendLine(CultureInfo.InvariantCulture,
            $"Item {item.ItemCode}: qty={item.Quantity.ToString("F2", CultureInfo.InvariantCulture)} " +
            $"unitUS={item.UnitUs.ToString("F4", CultureInfo.InvariantCulture)} " +
            $"totalUS={item.TotalUs.ToString("F4", CultureInfo.InvariantCulture)} " +
            $"rate={item.MonetaryRate.ToString("F4", CultureInfo.InvariantCulture)} " +
            $"totalBRL={item.TotalBrl.ToString("F2", CultureInfo.InvariantCulture)}");

        if (item.EvidencePhotoHashes.Count == 0)
        {
            document.AppendLine("  MISSING: no photo evidence on file for this item.");
            return;
        }

        foreach (var hash in item.EvidencePhotoHashes)
            document.AppendLine(CultureInfo.InvariantCulture, $"  Evidence hash: {hash}");
    }

    private static string ComputeDossierSignature(string documentText, string tenantSecret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(tenantSecret), Encoding.UTF8.GetBytes(documentText));
        return Convert.ToBase64String(hash);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
