namespace SiaWebhookApi.Configuration;

public sealed record SiaSettingsResponse(
    string IpAddress,
    int Port,
    bool EncryptionEnabled,
    string AccountNumber,
    string ReceiverNumber,
    string AccountPrefix,
    string EventCode);
