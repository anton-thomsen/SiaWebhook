namespace SiaWebhookApi.Services;

public sealed record SiaRuntimeSettingsOptions
{
    public string FilePath { get; init; } = "data/settings.json";

    public static SiaRuntimeSettingsOptions Load(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = Environment.GetEnvironmentVariable("SIA_SETTINGS_FILE")
            ?? configuration["SiaRuntimeSettings:FilePath"]
            ?? "data/settings.json";

        if (!Path.IsPathRooted(configured))
            configured = Path.Combine(environment.ContentRootPath, configured);

        return new SiaRuntimeSettingsOptions { FilePath = configured };
    }
}
