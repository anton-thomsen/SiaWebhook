namespace SiaWebhookApi.Configuration;

/// <summary>
/// What GET /api/settings returns. Deliberately NOT the internal SiaSettings:
/// the AES encryption key must never leave the server — the UI only needs to
/// know whether encryption is configured.
/// </summary>
public sealed record SiaSettingsResponse(
    string IpAddress,
    int Port,
    bool EncryptionEnabled,
    string AccountNumber,
    string ReceiverNumber,
    string AccountPrefix,
    string EventCode);
