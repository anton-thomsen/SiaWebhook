using System.Text.Json;

namespace SiaWebhookApi.Services;

public sealed class FileDeadLetterStore : IDeadLetterStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SiaDeliveryOptions _options;
    private readonly ILogger<FileDeadLetterStore> _logger;

    public FileDeadLetterStore(SiaDeliveryOptions options, ILogger<FileDeadLetterStore> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<string> SaveAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken = default)
    {
        var path = await WriteAsync(alarm, cancellationToken);
        _logger.LogCritical("Dead-lettered SIA alarm {DeadLetterId} at {Path}", alarm.Id, path);
        return alarm.Id;
    }

    public async Task<IReadOnlyList<DeadLetteredAlarm>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_options.DeadLetterDirectory))
            return [];

        var alarms = new List<DeadLetteredAlarm>();
        foreach (var path in Directory.EnumerateFiles(_options.DeadLetterDirectory, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var alarm = await JsonSerializer.DeserializeAsync<DeadLetteredAlarm>(stream, JsonOptions, cancellationToken);
                if (alarm is not null)
                    alarms.Add(alarm);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                _logger.LogError(ex, "Failed to read dead-letter file {Path}", path);
            }
        }

        return alarms.OrderBy(a => a.CreatedUtc).ToArray();
    }

    public async Task UpdateAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken = default)
    {
        await WriteAsync(alarm, cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var path = GetPath(id);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string id) => Path.Combine(_options.DeadLetterDirectory, $"{id}.json");

    private async Task<string> WriteAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.DeadLetterDirectory);
        var path = GetPath(alarm.Id);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, alarm, JsonOptions, cancellationToken);
        return path;
    }
}
