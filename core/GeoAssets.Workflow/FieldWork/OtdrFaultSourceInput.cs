namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Either a raw <c>.SOR</c> trace to parse, or a direct optical distance already determined by an
/// OLT's SNMP LOS alarm (XD01-283's alternate trigger path). Real SNMP trap listening/MIB
/// integration against a specific OLT vendor is out of scope here — this only models the
/// resulting optical-distance value once some upstream system has already produced it, the same
/// "take the external value as a caller-supplied input" scope cut XD01-281's reconcile endpoint
/// made for BSS-reported occupancy pending XD01-280.
/// </summary>
public sealed record OtdrFaultSourceInput(byte[]? SorTraceBytes, double? DirectOpticalDistanceMeters);
