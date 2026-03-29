namespace SiaWebhookApi.Configuration;

public class SiaSettings
{
    public string? EncryptionKey { get; set; }
    public int Port { get; set; }
    public required string IpAddress { get; set; }
}
