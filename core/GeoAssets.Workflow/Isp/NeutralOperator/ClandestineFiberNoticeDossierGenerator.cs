using System.Globalization;
using System.Text;
using GeoAssets.Core.Isp.Compliance;
using GeoAssets.Core.Isp.NeutralOperator.Evidence;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// Builds the "Notificação de Irregularidade de Compartilhamento" dossier for a flagged
/// clandestine attachment (XD01-263): the watermarked evidence photo's reference, the pole's
/// coordinates and BDGD pole code, and the statutory 30-day countdown.
///
/// No PDF/document-generation library is a dependency of this project. XD01-261 (this same
/// epic, immediately prior) investigated adding one for a different purpose (burning a visual
/// watermark into image pixels) and found the obvious free option, SixLabors.ImageSharp,
/// requires a paid commercial license — it settled on Magick.NET-Q8-AnyCPU (Apache 2.0)
/// instead, for that narrower need. A PDF library is a separate, heavier dependency decision on
/// top of that (QuestPDF's free tier is revenue-threshold-gated, not unconditionally free for an
/// unknown production deployment; PdfSharp/MigraDoc's licensing has had public ambiguity;
/// iText is AGPL/commercial) — not something to add on this ticket's behalf without the same
/// deliberate verification XD01-261 did for its own dependency, which this ticket did not
/// re-run. Following the established precedent instead
/// (<see cref="GeoAssets.Core.Isp.Compliance.PoleAudit.PoleLegalDefenseDossierGenerator"/>,
/// <see cref="GeoAssets.Workflow.Contractors.EspelhoDeMedicaoDossierGenerator"/>): the document
/// itself is plain text, deterministic and easy to test; the host layer decides how to package
/// it (e.g. alongside the sealed photo as a zip). "The watermarked photo" is referenced by its
/// XD01-261 HMAC digest and capture metadata rather than embedded as binary image data in a text
/// document — the raw sealed JPEG bytes, when the caller has them, travel as a
/// <see cref="DossierFile"/> attachment instead, the same attachment pattern
/// <c>PoleLegalDefenseDossierGenerator</c> already uses for its own sealed evidence photos.
/// </summary>
public static class ClandestineFiberNoticeDossierGenerator
{
    private const string StatutoryWarningText =
        "NOTIFICAÇÃO DE IRREGULARIDADE DE COMPARTILHAMENTO: o cabo identificado nesta notificação " +
        "encontra-se instalado neste poste sem autorização de compartilhamento registrada " +
        "(\"cabo à revelia\"). O ocupante dispõe de 30 (trinta) dias corridos, a partir da data " +
        "de emissão desta notificação, para regularizar o compartilhamento junto ao operador " +
        "neutro, sob pena de remoção do cabo mediante ordem de retirada coordenada.";

    private const string CodIdAttributeKey = "COD_ID";

    /// <param name="poleFeature">The flagged attachment's pole. Must carry a <c>COD_ID</c> attribute and a point geometry.</param>
    /// <param name="evidenceMetadata">The sealed photo's capture metadata (XD01-261).</param>
    /// <param name="evidenceDigestBase64">The sealed photo's HMAC-SHA256 digest (XD01-261's <c>FieldPhotoEvidenceSealer.ComputeDigest</c> output).</param>
    /// <param name="sealedPhotoJpegBytes">The sealed JPEG bytes, if available to attach — optional; the document references the photo by digest regardless.</param>
    public static ClandestineFiberNoticeDossierResult Generate(
        GeoFeature poleFeature,
        FieldPhotoEvidenceMetadata evidenceMetadata,
        string evidenceDigestBase64,
        DateTimeOffset flaggedAtUtc,
        DateTimeOffset deadlineUtc,
        byte[]? sealedPhotoJpegBytes = null)
    {
        ArgumentNullException.ThrowIfNull(poleFeature);
        ArgumentNullException.ThrowIfNull(evidenceMetadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDigestBase64);

        if (poleFeature.Geometry is not GeoPoint point)
        {
            throw new ArgumentException(
                $"Pole feature '{poleFeature.Id}' must have a point geometry.", nameof(poleFeature));
        }

        if (!poleFeature.Properties.CustomAttributes.TryGetValue(CodIdAttributeKey, out var codId) ||
            string.IsNullOrWhiteSpace(codId))
        {
            throw new ArgumentException(
                $"Pole feature '{poleFeature.Id}' is missing a '{CodIdAttributeKey}' attribute.", nameof(poleFeature));
        }

        var document = new StringBuilder();
        document.AppendLine("NOTIFICAÇÃO DE IRREGULARIDADE DE COMPARTILHAMENTO");
        document.AppendLine(CultureInfo.InvariantCulture, $"Emitida: {flaggedAtUtc:O}");
        document.AppendLine(CultureInfo.InvariantCulture, $"Prazo final para regularização: {deadlineUtc:O}");
        document.AppendLine();
        document.AppendLine(CultureInfo.InvariantCulture, $"Poste (BDGD COD_ID): {codId}");
        document.AppendLine(CultureInfo.InvariantCulture, $"Coordenadas: ({point.Latitude.ToString("R", CultureInfo.InvariantCulture)}, {point.Longitude.ToString("R", CultureInfo.InvariantCulture)})");
        document.AppendLine();
        document.AppendLine("Evidência fotográfica selada:");
        document.AppendLine(CultureInfo.InvariantCulture, $"  Digest HMAC-SHA256: {evidenceDigestBase64}");
        document.AppendLine(CultureInfo.InvariantCulture, $"  Capturada em: {evidenceMetadata.UtcTimestamp:O}");
        document.AppendLine(CultureInfo.InvariantCulture, $"  Coordenadas da captura: ({evidenceMetadata.Latitude.ToString("R", CultureInfo.InvariantCulture)}, {evidenceMetadata.Longitude.ToString("R", CultureInfo.InvariantCulture)})");
        document.AppendLine(CultureInfo.InvariantCulture, $"  Técnico: {evidenceMetadata.InspectorId}");
        document.AppendLine();
        document.AppendLine(StatutoryWarningText);

        var attachments = new List<DossierFile>();
        if (sealedPhotoJpegBytes is { Length: > 0 })
        {
            attachments.Add(new DossierFile($"{poleFeature.Id}/evidence.jpg", sealedPhotoJpegBytes));
        }

        return new ClandestineFiberNoticeDossierResult(document.ToString(), deadlineUtc, attachments);
    }
}
