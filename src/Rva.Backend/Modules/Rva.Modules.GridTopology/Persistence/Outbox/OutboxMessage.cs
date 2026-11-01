namespace Rva.Modules.GridTopology.Persistence.Outbox;

/// <summary>
/// An integration event written in the same transaction as the change it describes (<c>topology.outbox_messages</c>).
/// The writer and relay are added in plan p1-s01e and p1-s01f.
/// </summary>
internal sealed class OutboxMessage
{
    private OutboxMessage() { }

    internal OutboxMessage(Guid eventId, string eventType, string payload)
    {
        EventId = eventId;
        EventType = eventType;
        Payload = payload;
    }

    public long Id { get; private set; }

    /// <summary>Sent with the event so consumers can drop duplicates; UUIDv7.</summary>
    public Guid EventId { get; private set; }

    /// <summary>Versioned event name, ex. <c>AssetRegistered.v1</c> (D19).</summary>
    public string EventType { get; private set; } = "";

    /// <summary>The event serialized as JSON.</summary>
    public string Payload { get; private set; } = "";

    /// <summary>Set by the database when the row is written.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }
    public int AttemptCount { get; private set; }
}
