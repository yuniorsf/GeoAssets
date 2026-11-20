using System.Text.Json.Serialization;

namespace GeoAssets.IpBilling.GrantSync;

/// <summary>
/// Where a município's Prefeitura grant stands against its saved link (XD01-395), derived from the latest grant-sync
/// request queued for it. Serialized as <c>none|pending|synced|failed</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MunicipioGrantSyncState>))]
public enum MunicipioGrantSyncState
{
    /// <summary>No save of this município ever needed a grant change.</summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>The latest change is queued or being retried; the kernel's grant may still be the previous one.</summary>
    [JsonStringEnumMemberName("pending")]
    Pending,

    /// <summary>The kernel's grant matches the latest saved link.</summary>
    [JsonStringEnumMemberName("synced")]
    Synced,

    /// <summary>The latest change ran out of attempts; repair it through <c>POST /api/ip-billing/municipio-grants/sync</c> or save the município again.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,
}
