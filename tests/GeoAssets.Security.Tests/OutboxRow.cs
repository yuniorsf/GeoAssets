using GeoAssets.Core.Messaging;

namespace GeoAssets.Security.Tests;

/// <summary>One row of the workflow outbox, as the saga end-to-end tests read it.</summary>
internal sealed record OutboxRow(
    Guid Id, string MessageType, OutboxMessageStatus Status, int AttemptCount, string? LastError, DateTime CreatedAtUtc, string? TraceParent);
