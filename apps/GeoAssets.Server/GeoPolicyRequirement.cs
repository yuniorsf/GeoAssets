using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// Wraps an <see cref="Identity.Authorization.Models.AppPolicy"/> name so
/// <see cref="GeoAuthorizationHandler"/> knows which policy to evaluate against
/// <c>IGeoAuthorizationService.EvaluatePolicyAsync</c> (XD01-13).
/// </summary>
public sealed class GeoPolicyRequirement(string policyName) : IAuthorizationRequirement
{
    public string PolicyName { get; } = policyName;
}
