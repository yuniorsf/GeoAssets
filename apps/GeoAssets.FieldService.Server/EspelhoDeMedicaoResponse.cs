namespace GeoAssets.FieldService.Server;

/// <summary>Monthly "Espelho de Medição" statement: a contractor's ledger entries for one calendar month plus their summed value.</summary>
internal sealed record EspelhoDeMedicaoResponse(
    int Year,
    int Month,
    decimal TotalValue,
    List<ContractorUsLedgerEntryResponse> Entries);
