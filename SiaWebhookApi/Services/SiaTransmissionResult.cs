namespace SiaWebhookApi.Services;

/// <summary>
/// Outcome of one TCP transmission attempt. "Transmitted but NAK'd" and
/// "couldn't connect" are both failures — callers must not have to parse
/// magic strings to tell success from failure.
/// </summary>
public sealed record SiaTransmissionResult(bool Success, string? Response, string? Error)
{
    public static SiaTransmissionResult Ack(string response) => new(true, response, null);

    public static SiaTransmissionResult Failed(string error, string? response = null) => new(false, response, error);
}
