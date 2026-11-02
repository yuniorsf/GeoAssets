namespace GeoAssets.FieldService.Server;

internal sealed record PoleLicenseRecordRequest(
    string ScmLicenseNumber, string LicenseHolderName, DateTimeOffset IssuedAtUtc, DateTimeOffset? ExpiresAtUtc);
