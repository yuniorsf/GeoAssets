using System.Text.Json;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Persistence.Entities;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// Converts between the domain <see cref="ContractorUsLedgerEntry"/> and the EF entity
/// <see cref="ContractorUsLedgerEntryRecord"/>. Photo proof is flattened to plain
/// longitude/latitude doubles for storage rather than round-tripping full
/// <see cref="GeoPoint"/>/NTS geometry JSON — this is an internal persistence encoding, not
/// the GeoJSON wire format <c>GeoFeature.Geometry</c> serialization relies on.
/// </summary>
internal static class ContractorUsLedgerMapper
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private sealed record PhotoProofJson(string Url, double Longitude, double Latitude, DateTime CapturedAt);

    public static ContractorUsLedgerEntryRecord ToRecord(ContractorUsLedgerEntry entry) => new()
    {
        Id = entry.Id,
        OrganizationId = entry.OrganizationId,
        ServiceOrderId = entry.ServiceOrderId,
        UsCode = entry.UsCode,
        Description = entry.Description,
        Quantity = entry.Quantity,
        UnitValue = entry.UnitValue,
        PhotoProofJson = JsonSerializer.Serialize(
            entry.PhotoProof.Select(p => new PhotoProofJson(p.Url, p.Location.Longitude, p.Location.Latitude, p.CapturedAt)),
            _json),
        CreatedAt = entry.CreatedAt,
        UpdatedAt = entry.UpdatedAt,
    };

    public static ContractorUsLedgerEntry ToDomain(ContractorUsLedgerEntryRecord record)
    {
        var photos = JsonSerializer.Deserialize<List<PhotoProofJson>>(record.PhotoProofJson, _json) ?? [];

        return new ContractorUsLedgerEntry
        {
            Id = record.Id,
            OrganizationId = record.OrganizationId,
            ServiceOrderId = record.ServiceOrderId,
            UsCode = record.UsCode,
            Description = record.Description,
            Quantity = record.Quantity,
            UnitValue = record.UnitValue,
            PhotoProof = photos
                .Select(p => new LedgerPhotoProof(p.Url, new GeoPoint(p.Longitude, p.Latitude), p.CapturedAt))
                .ToList(),
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
        };
    }
}
