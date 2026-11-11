namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>
/// A generated NF Com (telecom-service tax invoice) document. <paramref name="Xml"/> is
/// well-formed and internally consistent, but validated against a GeoAssets-authored
/// placeholder schema, NOT the real SEFAZ NFCom layout 4.00 XSD — see
/// <see cref="NfComDocumentGenerator"/>'s remarks for why. <paramref name="DocumentNumber"/>
/// is GeoAssets-assigned (sequential per tenant+period), not a SEFAZ-issued series/number.
/// </summary>
public sealed record NfComDocumentResult(string DocumentNumber, string Xml, DateTimeOffset IssuedAtUtc);
