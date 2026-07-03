using System.Text.Json;
using System.Text.Json.Nodes;
using SiaWebhookApi.Configuration;

namespace SiaWebhookApi.Services;

public class SiaSettingsService
{
    private SiaSettings _current;
    private readonly SiaRuntimeSettingsOptions _options;
    private readonly ILogger<SiaSettingsService> _logger;
    private readonly ISiaTransmitter _transmitter;
    private readonly object _lock = new();

    public SiaSettingsService(
        IWebHostEnvironment env,
        ILogger<SiaSettingsService> logger,
        IConfiguration configuration,
        ISiaTransmitter transmitter,
        SiaRuntimeSettingsOptions options)
    {
        _logger = logger;
        _transmitter = transmitter;
        _options = options;
        _current = configuration.GetSection("SiaSettings").Get<SiaSettings>()
            ?? new SiaSettings { IpAddress = "", Port = 0, EncryptionKey = null };
        OverlayPersistedSettings();
    }

    public SiaSettingsResponse GetPublic()
    {
        var current = GetForTransmission();
        return new SiaSettingsResponse(
            current.IpAddress,
            current.Port,
            !string.IsNullOrEmpty(current.EncryptionKey),
            current.AccountNumber,
            current.ReceiverNumber,
            current.AccountPrefix,
            current.EventCode);
    }

    public SiaSettings GetForTransmission()
    {
        SiaSettings copy;
        lock (_lock)
        {
            copy = new SiaSettings
            {
                IpAddress      = _current.IpAddress,
                Port           = _current.Port,
                EncryptionKey  = null,
                AccountNumber  = _current.AccountNumber,
                ReceiverNumber = _current.ReceiverNumber,
                AccountPrefix  = _current.AccountPrefix,
                EventCode      = _current.EventCode
            };
        }

        var envIp = Environment.GetEnvironmentVariable("SIA_RECEIVER_IP");
        if (!string.IsNullOrWhiteSpace(envIp))
            copy.IpAddress = envIp;

        var envPort = Environment.GetEnvironmentVariable("SIA_RECEIVER_PORT");
        if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out var parsedPort))
            copy.Port = parsedPort;

        // Environment variable takes precedence — keeps the secret out of appsettings.json in prod.
        var envKey = Environment.GetEnvironmentVariable("SIA_ENCRYPTION_KEY");
        if (!string.IsNullOrEmpty(envKey))
            copy.EncryptionKey = envKey;
        return copy;
    }

    public async Task<(bool success, string? error)> UpdateAsync(SiaSettingsUpdateRequest updated)
    {
        if (HasReservedChars(updated.AccountNumber, updated.ReceiverNumber, updated.AccountPrefix, updated.EventCode))
            return (false, "SIA identity fields cannot contain reserved delimiter/control characters.");

        SiaSettings next;
        lock (_lock)
        {
            next = new SiaSettings
            {
                IpAddress      = _current.IpAddress,
                Port           = _current.Port,
                EncryptionKey  = null,
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

    private async Task PersistToDiskAsync(SiaSettings settings)
    {
        var persisted = new JsonObject
        {
            ["AccountNumber"]  = settings.AccountNumber,
            ["ReceiverNumber"] = settings.ReceiverNumber,
            ["AccountPrefix"]  = settings.AccountPrefix,
            ["EventCode"]      = settings.EventCode
        };
        string json = persisted.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        // Write to temp then atomically replace — prevents a partial write from corrupting runtime settings.
        Directory.CreateDirectory(Path.GetDirectoryName(_options.FilePath)!);
        string tempPath = _options.FilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, _options.FilePath, overwrite: true);
    }

    public async Task<(bool success, string? error)> TestConnectionAsync()
    {
        var s = GetForTransmission();
        try
        {
            var result = await _transmitter.SendAsync(s.IpAddress, s.Port, "");
            return result.Success ? (true, null) : (false, result.Error ?? result.Response);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static bool HasReservedChars(params string[] values) =>
        values.Any(v => string.IsNullOrWhiteSpace(v) || v.IndexOfAny(['\r', '\n', '[', ']', '|', '_']) >= 0);

    private void OverlayPersistedSettings()
    {
        if (!File.Exists(_options.FilePath))
            return;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_options.FilePath));
            if (root is null)
                return;

            _current.AccountNumber = root["AccountNumber"]?.GetValue<string>() ?? _current.AccountNumber;
            _current.ReceiverNumber = root["ReceiverNumber"]?.GetValue<string>() ?? _current.ReceiverNumber;
            _current.AccountPrefix = root["AccountPrefix"]?.GetValue<string>() ?? _current.AccountPrefix;
            _current.EventCode = root["EventCode"]?.GetValue<string>() ?? _current.EventCode;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogError(ex, "Failed to load persisted SIA runtime settings from {Path}", _options.FilePath);
        }
    }
}
