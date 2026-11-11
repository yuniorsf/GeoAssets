namespace GeoAssets.Workflow.Erp.Totvs;

/// <summary>One piece of the technician's assigned equipment's calibration expiry.</summary>
public sealed record EquipmentCalibrationStatus(string EquipmentId, DateTime ExpiresOnUtc);
