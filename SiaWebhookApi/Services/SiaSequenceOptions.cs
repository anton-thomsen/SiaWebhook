namespace SiaWebhookApi.Services;

public sealed record SiaSequenceOptions
{
    public string? FilePath { get; init; }

    public static SiaSequenceOptions Load(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = Environment.GetEnvironmentVariable("SIA_SEQUENCE_FILE")
            ?? configuration["SiaSequence:FilePath"];

        if (string.IsNullOrWhiteSpace(configured))
            configured = "data/sia-sequence.txt";

        if (!Path.IsPathRooted(configured))
            configured = Path.Combine(environment.ContentRootPath, configured);

        return new SiaSequenceOptions { FilePath = configured };
    }
}
