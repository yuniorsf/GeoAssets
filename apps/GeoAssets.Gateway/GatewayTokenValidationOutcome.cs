namespace GeoAssets.Gateway;

/// <summary>
/// What the gateway could establish about a request's bearer token before keying its rate-limit bucket (XD01-398).
/// Only <see cref="Valid"/> and <see cref="Unvalidated"/> claims choose the bucket; every other outcome keys by the client
/// address. None of them rejects the request — authorization stays with the backends.
/// </summary>
internal enum GatewayTokenValidationOutcome
{
    /// <summary>No bearer token, or one that isn't a JWT at all (undecodable).</summary>
    Absent,

    /// <summary>The token's signature, issuer and lifetime validated against the CIAM tenant's signing keys.</summary>
    Valid,

    /// <summary>A JWT that failed validation: unknown key or bad signature, wrong issuer, expired or not yet valid, malformed.</summary>
    Invalid,

    /// <summary>Validation is configured but the signing keys couldn't be obtained (metadata unreachable, or backing off after a failure).</summary>
    Unavailable,

    /// <summary>Validation is not configured: the token was decoded only (the pre-XD01-398 behaviour).</summary>
    Unvalidated,
}
