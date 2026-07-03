using SiaWebhookApi.Models;

namespace SiaWebhookApi.Services;

public sealed record DeadLetteredAlarm
{
    public required string Id { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastAttemptUtc { get; init; } = DateTimeOffset.UtcNow;
    public int Attempts { get; init; }
    public required DataTransferObject Webhook { get; init; }
    public required string SiaMessage { get; init; }
    public required string SequenceNumber { get; init; }
    public required string AccountNumber { get; init; }
    public required string ReceiverEndpoint { get; init; }
    public required bool Encrypted { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}
