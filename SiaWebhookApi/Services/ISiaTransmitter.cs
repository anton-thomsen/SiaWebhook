namespace SiaWebhookApi.Services;

public interface ISiaTransmitter
{
    Task<string> SendAsync(string ipAddress, int port, string message);
}
