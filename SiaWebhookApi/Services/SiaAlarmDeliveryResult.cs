namespace SiaWebhookApi.Services;

public sealed record SiaAlarmDeliveryResult(
    bool Success,
    string? Response,
    string? Error,
    string? DeadLetterId,
    IReadOnlyList<string> AttemptErrors)
{
    public static SiaAlarmDeliveryResult Delivered(string? response, IReadOnlyList<string> attemptErrors) =>
        new(true, response, null, null, attemptErrors);

    public static SiaAlarmDeliveryResult Failed(string error, string deadLetterId, IReadOnlyList<string> attemptErrors) =>
        new(false, null, error, deadLetterId, attemptErrors);
}
