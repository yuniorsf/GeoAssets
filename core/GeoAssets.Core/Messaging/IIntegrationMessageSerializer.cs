namespace GeoAssets.Core.Messaging;

/// <summary>
/// Encodes and decodes strongly-typed integration messages to and from a binary wire representation.
/// </summary>
public interface IIntegrationMessageSerializer
{
    string ContentType { get; }

    byte[] Serialize<TMessage>(TMessage message)
        where TMessage : notnull;

    TMessage Deserialize<TMessage>(ReadOnlyMemory<byte> payload)
        where TMessage : notnull;
}
