namespace SiaWebhookApi.Services;

public sealed record SiaDeliveryOptions
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(2);
    public string DeadLetterDirectory { get; init; } = "data/dead-letters";
    public string? AlertWebhookUrl { get; init; }
    public TimeSpan AlertTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan BackgroundRetryInterval { get; init; } = TimeSpan.FromSeconds(30);

    public static SiaDeliveryOptions Load(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var section = configuration.GetSection("SiaDelivery").Get<SiaDeliveryOptions>() ?? new SiaDeliveryOptions();
        var deadLetterDirectory = Environment.GetEnvironmentVariable("SIA_DEAD_LETTER_DIR")
            ?? section.DeadLetterDirectory;

        if (!Path.IsPathRooted(deadLetterDirectory))
            deadLetterDirectory = Path.Combine(environment.ContentRootPath, deadLetterDirectory);

        return section with
        {
            DeadLetterDirectory = deadLetterDirectory,
            AlertWebhookUrl = Environment.GetEnvironmentVariable("ALERT_WEBHOOK_URL") ?? section.AlertWebhookUrl
        };
    }
}
