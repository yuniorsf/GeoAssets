using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Registry;

/// <summary>The rules a vertical's descriptor set must meet before the registry takes it (XD01-369).</summary>
public static class OrderTypeDescriptorValidation
{
    /// <summary>Returns why the set is invalid, or <c>null</c> when it is valid.</summary>
    public static string? FindProblem(string verticalId, IReadOnlyCollection<OrderTypeDescriptor>? descriptors)
    {
        if (string.IsNullOrWhiteSpace(verticalId)) return "The vertical id is blank.";
        if (descriptors is null) return "The descriptor set is missing.";

        var orderTypeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (descriptor is null) return "A descriptor is missing.";
            if (!string.Equals(descriptor.VerticalId, verticalId, StringComparison.Ordinal))
                return $"Descriptor '{descriptor.OrderTypeId}' names vertical '{descriptor.VerticalId}', not '{verticalId}'.";
            if (string.IsNullOrWhiteSpace(descriptor.OrderTypeId)) return "A descriptor's order type id is blank.";
            if (!orderTypeIds.Add(descriptor.OrderTypeId)) return $"Order type '{descriptor.OrderTypeId}' is described twice.";
            if (string.IsNullOrWhiteSpace(descriptor.DisplayNameKey) || string.IsNullOrWhiteSpace(descriptor.ApiBaseRoute))
                return $"Descriptor '{descriptor.OrderTypeId}' needs a display name key and an API base route.";
            if (descriptor.FeatureActions.Select(a => ActionProblem(descriptor.OrderTypeId, a)).FirstOrDefault(p => p is not null) is { } problem)
                return problem;
            if (TargetProblem(descriptor) is { } targetProblem)
                return targetProblem;
        }

        return null;
    }

    /// <summary>
    /// XD01-379: a published target policy meets the rules an in-process order type's does
    /// (<see cref="Targets.OrderTargetPolicy.Validate"/>), and no terminal status is blank.
    /// </summary>
    private static string? TargetProblem(OrderTypeDescriptor descriptor)
    {
        if (descriptor.TerminalStatuses.Any(string.IsNullOrWhiteSpace))
            return $"Descriptor '{descriptor.OrderTypeId}' lists a blank terminal status.";

        try
        {
            descriptor.TargetPolicy?.Validate(descriptor.OrderTypeId);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private static string? ActionProblem(string orderTypeId, FeatureActionDescriptor? action) => action switch
    {
        null => $"A feature action of '{orderTypeId}' is missing.",
        { Role: var role } when !AssetTypeRoles.IsWellFormed(role) => $"Feature action role '{role}' of '{orderTypeId}' is not a valid asset-type role.",
        _ when string.IsNullOrWhiteSpace(action.Verb) || string.IsNullOrWhiteSpace(action.LabelKey)
            || string.IsNullOrWhiteSpace(action.OrderTypeId) || string.IsNullOrWhiteSpace(action.Route)
            => $"A feature action of '{orderTypeId}' needs a verb, a label key, an order type id and a route.",
        _ => null,
    };
}
