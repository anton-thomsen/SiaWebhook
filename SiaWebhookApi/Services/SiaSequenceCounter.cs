namespace SiaWebhookApi.Services;

public sealed class SiaSequenceCounter
{
    private readonly string? _filePath;
    private readonly ILogger<SiaSequenceCounter>? _logger;
    private readonly object _lock = new();
    private int _counter;

    public SiaSequenceCounter()
    {
    }

    public SiaSequenceCounter(SiaSequenceOptions options, ILogger<SiaSequenceCounter> logger)
    {
        _filePath = options.FilePath;
        _logger = logger;
        _counter = LoadLastSequence(_filePath);
    }

    /// <summary>Returns the next sequence number as a 4-digit string, wrapping 9999→0001.</summary>
    public string Next()
    {
        lock (_lock)
        {
            _counter = (_counter % 9999) + 1;
            PersistLastSequence(_counter);
            return _counter.ToString("D4");
        }
    }

    private int LoadLastSequence(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return 0;

        try
        {
            var raw = File.ReadAllText(filePath).Trim();
            if (int.TryParse(raw, out var value) && value is >= 0 and <= 9999)
                return value;

            _logger?.LogWarning("Ignoring invalid SIA sequence value in {Path}: {Value}", filePath, raw);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to load SIA sequence from {Path}", filePath);
        }

        return 0;
    }

    private void PersistLastSequence(int sequence)
    {
        if (string.IsNullOrWhiteSpace(_filePath))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, sequence.ToString("D4"));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to persist SIA sequence to {Path}", _filePath);
        }
    }
}
