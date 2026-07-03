namespace SiaWebhookApi.Services;

public sealed class SiaSequenceCounter
{
    private int _counter;

    /// <summary>Returns the next sequence number as a 4-digit string, wrapping 9999→0001.</summary>
    public string Next()
    {
        int n = Interlocked.Increment(ref _counter);
        return (((n - 1) % 9999) + 1).ToString("D4");
    }
}
