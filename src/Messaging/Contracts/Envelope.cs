namespace Portal.Messaging.Contracts;

/// <summary>Umschlag für fachliche Events. <see cref="MessageId"/> ist der Schlüssel für die Inbox der Consumer.</summary>
public sealed record Envelope<T>(Guid MessageId, string Type, int Version, DateTimeOffset OccurredAt, T Payload);
