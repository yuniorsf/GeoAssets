namespace GeoAssets.Tools.SharedUseImport;

/// <param name="Cnpj">A CNPJ, punctuated or not: 14 digits.</param>
/// <param name="OrganizationId">The GeoAssets organization registered under it.</param>
internal sealed record OrganizationMapping(string Cnpj, Guid OrganizationId);
