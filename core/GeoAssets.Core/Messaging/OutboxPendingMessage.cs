namespace GeoAssets.Core.Messaging;

/// <summary>
/// Represents an outbox message claimed under a bounded worker lease for broker publication.
/// </summary>
public sealed record OutboxPendingMessage(
    IntegrationEnvelope Envelope,
    int AttemptCount,
    DateTimeOffset? LockedUntilUtc = null);
