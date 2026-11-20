using System.Text.Json.Serialization;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>What <c>PUT {prefix}/organization-grants</c> (XD01-385) did.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrganizationGrantUpsertOutcome>))]
public enum OrganizationGrantUpsertOutcome
{
    /// <summary>No active grant had the key; one was created.</summary>
    Created,

    /// <summary>The active grant with the key was brought to the requested actions, role and expiry.</summary>
    Updated,

    /// <summary>The active grant with the key already matched the request.</summary>
    Unchanged,
}
