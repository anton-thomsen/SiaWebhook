using System.Text.Json;
using System.Text.Json.Nodes;
using SiaWebhookApi.Configuration;

namespace SiaWebhookApi.Services;

public class SiaSettingsService
{
    private SiaSettings _current;
    private readonly string _appsettingsPath;
    private readonly ILogger<SiaSettingsService> _logger;
    private readonly ISiaTransmitter _transmitter;
    private readonly object _lock = new();

    public SiaSettingsService(
        IWebHostEnvironment env,
        ILogger<SiaSettingsService> logger,
        IConfiguration configuration,
        ISiaTransmitter transmitter)
    {
        _logger = logger;
        _transmitter = transmitter;
        _appsettingsPath = Path.Combine(env.ContentRootPath, "appsettings.json");
        _current = configuration.GetSection("SiaSettings").Get<SiaSettings>()
            ?? new SiaSettings { IpAddress = "", Port = 0, EncryptionKey = null };
    }

    /// <summary>
    /// Settings safe to return to the admin UI: the encryption key is reduced
    /// to a configured/not-configured boolean and never leaves the server.
    /// </summary>
    public SiaSettingsResponse GetPublic()
    {
        var current = Get();
        return new SiaSettingsResponse(
            current.IpAddress,
            current.Port,
            !string.IsNullOrEmpty(current.EncryptionKey),
            current.AccountNumber,
            current.ReceiverNumber,
            current.AccountPrefix,
            current.EventCode);
    }

    public SiaSettings Get()
    {
        SiaSettings copy;
        lock (_lock)
        {
            copy = new SiaSettings
            {
                IpAddress      = _current.IpAddress,
                Port           = _current.Port,
                EncryptionKey  = _current.EncryptionKey,
                AccountNumber  = _current.AccountNumber,
                ReceiverNumber = _current.ReceiverNumber,
                AccountPrefix  = _current.AccountPrefix,
                EventCode      = _current.EventCode
            };
        }
        // Receiver routing is env-managed on the VPS: env vars always win, so a
        // compromised admin session can never divert alarms to another endpoint.
        var envIp = Environment.GetEnvironmentVariable("SIA_RECEIVER_IP");
        if (!string.IsNullOrWhiteSpace(envIp))
            copy.IpAddress = envIp;

        var envPort = Environment.GetEnvironmentVariable("SIA_RECEIVER_PORT");
        if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out var parsedPort))
            copy.Port = parsedPort;

        // Environment variable takes precedence — keeps the secret out of appsettings.json in prod
        var envKey = Environment.GetEnvironmentVariable("SIA_ENCRYPTION_KEY");
        if (!string.IsNullOrEmpty(envKey))
            copy.EncryptionKey = envKey;
        return copy;
    }

    public async Task<(bool success, string? error)> UpdateAsync(SiaSettingsUpdateRequest updated)
    {
        // Only harmless identity/event fields are UI-writable. Receiver IP/port
        // and the encryption key are not part of the request type at all —
        // they are env-managed and cannot be changed through the API.
        if (HasReservedChars(updated.AccountNumber, updated.ReceiverNumber, updated.AccountPrefix, updated.EventCode))
            return (false, "SIA identity fields are required and cannot contain reserved delimiter/control characters.");

        SiaSettings next;
        lock (_lock)
        {
            next = new SiaSettings
            {
                IpAddress      = _current.IpAddress,
                Port           = _current.Port,
                EncryptionKey  = _current.EncryptionKey,
                AccountNumber  = updated.AccountNumber,
                ReceiverNumber = updated.ReceiverNumber,
                AccountPrefix  = updated.AccountPrefix,
                EventCode      = updated.EventCode
            };
        }

        try
        {
            await PersistToDiskAsync(next);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist SiaSettings to appsettings.json");
            return (false, ex.Message);
        }

        lock (_lock)
        {
            _current = next;
        }

        _logger.LogInformation("SiaSettings updated: account={Account} receiver={Receiver} event={Event}",
            updated.AccountNumber, updated.ReceiverNumber, updated.EventCode);
        return (true, null);
    }

    private static bool HasReservedChars(params string[] values) =>
        values.Any(v => string.IsNullOrWhiteSpace(v) || v.IndexOfAny(['\r', '\n', '[', ']', '|', '_']) >= 0);

    private async Task PersistToDiskAsync(SiaSettings settings)
    {
        string rawJson = await File.ReadAllTextAsync(_appsettingsPath);
        JsonNode root = JsonNode.Parse(rawJson)!;
        root["SiaSettings"] = new JsonObject
        {
            ["IpAddress"]      = settings.IpAddress,
            ["Port"]           = settings.Port,
            ["EncryptionKey"]  = settings.EncryptionKey ?? string.Empty,
            ["AccountNumber"]  = settings.AccountNumber,
            ["ReceiverNumber"] = settings.ReceiverNumber,
            ["AccountPrefix"]  = settings.AccountPrefix,
            ["EventCode"]      = settings.EventCode
        };
        string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        // Write to temp then atomically replace — prevents a partial write from corrupting appsettings.json
        string tempPath = _appsettingsPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Replace(tempPath, _appsettingsPath, null);
    }

    public async Task<(bool success, string? error)> TestConnectionAsync()
    {
        var s = Get();
        try
        {
            string result = await _transmitter.SendAsync(s.IpAddress, s.Port, "");
            return result.StartsWith("ERROR:") ? (false, result) : (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
