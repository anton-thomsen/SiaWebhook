namespace SiaWebhookApi.Configuration;

public sealed class SiaSettingsUpdateRequest
{
    public string AccountNumber { get; set; } = "A001";
    public string ReceiverNumber { get; set; } = "R0";
    public string AccountPrefix { get; set; } = "L0";
    public string EventCode { get; set; } = "NPA";
}
