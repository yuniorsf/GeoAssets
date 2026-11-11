namespace GeoAssets.Workflow.Erp.SapWeBuy;

/// <summary>Result of <see cref="SapWeBuyClearingPackageExporter.Export"/> (XD01-255).</summary>
public sealed record SapWeBuyClearingPackageResult(string ServiceOrderId, string FileName, byte[] XmlBytes);
