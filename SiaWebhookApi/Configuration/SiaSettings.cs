namespace SiaWebhookApi.Configuration;

public class SiaSettings
{
    public string? EncryptionKey { get; set; }
    public int Port { get; set; }
    public required string IpAddress { get; set; }

    // SIA message identity fields
    public string AccountNumber { get; set; } = "A001";
    public string ReceiverNumber { get; set; } = "R0";
    public string AccountPrefix { get; set; } = "L0";
    public string EventCode { get; set; } = "NPA";
}
