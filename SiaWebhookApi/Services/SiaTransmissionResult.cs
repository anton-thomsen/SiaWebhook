namespace SiaWebhookApi.Services;

public sealed record SiaTransmissionResult(bool Success, string? Response, string? Error)
{
    public static SiaTransmissionResult Ack(string response) => new(true, response, null);

    public static SiaTransmissionResult Failed(string error, string? response = null) => new(false, response, error);
}
