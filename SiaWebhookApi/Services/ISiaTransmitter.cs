namespace SiaWebhookApi.Services;

public interface ISiaTransmitter
{
    Task<SiaTransmissionResult> SendAsync(string ipAddress, int port, string message, CancellationToken cancellationToken = default);
}
