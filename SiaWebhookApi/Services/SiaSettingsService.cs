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
        // Environment variable takes precedence — keeps the secret out of appsettings.json in prod
        var envKey = Environment.GetEnvironmentVariable("SIA_ENCRYPTION_KEY");
        if (!string.IsNullOrEmpty(envKey))
            copy.EncryptionKey = envKey;
        return copy;
    }

    public async Task<(bool success, string? error)> UpdateAsync(SiaSettings updated)
    {
        // The key never round-trips through the UI: whatever the client sends
        // is ignored, so a settings save can neither set nor wipe it.
        SiaSettings next;
        lock (_lock)
        {
            next = new SiaSettings
            {
                IpAddress      = updated.IpAddress,
                Port           = updated.Port,
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

        _logger.LogInformation("SiaSettings updated: IP={Ip} Port={Port}", updated.IpAddress, updated.Port);
        return (true, null);
    }

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
