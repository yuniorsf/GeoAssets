using System.Text.Json;
using GeoAssets.Core.Interfaces;

namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Reads an inspected order's claiming contractor organization and posted Bill-of-Quantities
/// line items from <see cref="IServiceOrder.Attributes"/> (XD01-228's
/// <c>InspectionCommitService</c>, in the EFCore persistence layer) — neither has a dedicated
/// <see cref="GeoAssets.Workflow.Orders.ServiceOrder"/> column yet, so this follows the same
/// attribute-bag convention as <c>SlaGuardEngine</c>'s own stamped attributes: free for orders
/// that never need it, no migration required. Documented here since nothing else defines the
/// convention:
/// <code>
///   Attributes["contractorOrganizationId"] = "11111111-1111-1111-1111-111111111111"
///   Attributes["boqLines"] = "[{\"itemCode\":\"US-EL-014\",\"quantity\":1}]"
/// </code>
/// </summary>
public static class ServiceOrderBoqAttributes
{
    public const string ContractorOrganizationIdKey = "contractorOrganizationId";
    public const string BoqLinesKey = "boqLines";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static Guid ReadContractorOrganizationId(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (!attributes.TryGetValue(ContractorOrganizationIdKey, out var raw) || !Guid.TryParse(raw, out var id))
        {
            throw new InvalidOperationException(
                $"ServiceOrder.Attributes['{ContractorOrganizationIdKey}'] is missing or not a valid GUID.");
        }

        return id;
    }

    public static IReadOnlyList<(string ItemCode, decimal Quantity)> ReadBoqLines(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (!attributes.TryGetValue(BoqLinesKey, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var parsed = JsonSerializer.Deserialize<List<BoqLineDto>>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"ServiceOrder.Attributes['{BoqLinesKey}'] could not be parsed as a BoQ line array.");

        return parsed.ConvertAll(l => (l.ItemCode, l.Quantity));
    }

    private sealed record BoqLineDto(string ItemCode, decimal Quantity);
}
