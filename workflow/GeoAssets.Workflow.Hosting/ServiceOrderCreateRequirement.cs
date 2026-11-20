using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// "May the caller create an order of this type?" as an ASP.NET Core requirement (XD01-364), answered by
/// <see cref="ServiceOrderCreateAuthorizationHandler"/> with <c>ServiceOrderRules.CanCreate</c> over the type's
/// <c>CreationPolicies</c> — not by a permission any role can hold.
/// </summary>
public sealed class ServiceOrderCreateRequirement : IAuthorizationRequirement
{
    public ServiceOrderCreateRequirement(string orderTypeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderTypeId);
        OrderTypeId = orderTypeId;
    }

    public string OrderTypeId { get; }
}
