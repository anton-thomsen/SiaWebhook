namespace SiaWebhookApi.Configuration;

/// <summary>
/// Holds all authentication configuration. Non-secret values (issuer, audience,
/// token lifetime) come from the "Auth" section of appsettings.json. The three
/// secrets are sourced from environment variables only — never from the persisted
/// appsettings.json, which the settings UI rewrites and which lives in source control.
/// </summary>
public class AuthOptions
{
    public string Issuer { get; set; } = "SiaWebhookApi";
    public string Audience { get; set; } = "SiaWebhookApi";
    public int TokenLifetimeHours { get; set; } = 8;

    // --- Secrets (env vars only) ---
    public string JwtSecret { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string ZepcamApiKey { get; set; } = string.Empty;

    // Insecure development fallbacks. Used only when the matching env var is unset,
    // accompanied by a loud startup warning. NEVER rely on these in production.
    private const string DevJwtSecret = "dev-insecure-jwt-signing-key-change-me-0123456789ABCDEF";
    private const string DevAdminPassword = "admin";
    private const string DevZepcamApiKey = "dev-zepcam-key";

    /// <summary>
    /// Builds AuthOptions from configuration + environment variables. In Production a
    /// missing secret is a fatal startup error; in Development it falls back to an
    /// insecure default with a loud warning so the app still runs locally.
    /// </summary>
    public static AuthOptions Load(IConfiguration config, ILogger logger, bool isProduction)
    {
        var opts = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();

        opts.JwtSecret     = ResolveSecret(logger, "SIA_JWT_SECRET", DevJwtSecret, isProduction);
        opts.AdminPassword = ResolveSecret(logger, "SIA_ADMIN_PASSWORD", DevAdminPassword, isProduction);
        opts.ZepcamApiKey  = ResolveSecret(logger, "ZEPCAM_API_KEY", DevZepcamApiKey, isProduction);

        // HS256 needs a key of at least 256 bits (32 bytes / 32 ASCII chars).
        if (opts.JwtSecret.Length < 32)
        {
            const string msg = "SIA_JWT_SECRET must be at least 32 characters (256-bit key) for HS256.";
            if (isProduction)
                throw new InvalidOperationException(msg);
            logger.LogWarning("{Message} Current length: {Length}. Token signing may fail.", msg, opts.JwtSecret.Length);
        }

        ValidateAdminPassword(logger, opts.AdminPassword, isProduction);

        return opts;
    }

    private static string ResolveSecret(ILogger logger, string envName, string devDefault, bool isProduction)
    {
        var fromEnv = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrEmpty(fromEnv))
            return fromEnv;

        if (isProduction)
            throw new InvalidOperationException(
                $"Required auth secret {envName} is not set. Configure it as an environment variable before deploying.");

        logger.LogWarning(
            "Auth secret {EnvVar} is not set — using an INSECURE development default. " +
            "Set {EnvVar} as an environment variable before deploying to production.", envName, envName);
        return devDefault;
    }

    private static void ValidateAdminPassword(ILogger logger, string password, bool isProduction)
    {
        var problems = new List<string>();
        if (password.Length < 12)
            problems.Add("at least 12 characters");
        if (!password.Any(char.IsUpper))
            problems.Add("one uppercase letter");
        if (!password.Any(char.IsLower))
            problems.Add("one lowercase letter");
        if (!password.Any(char.IsDigit))
            problems.Add("one digit");
        if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
            problems.Add("one symbol");
        if (string.Equals(password, DevAdminPassword, StringComparison.Ordinal))
            problems.Add("not the development default");

        if (problems.Count == 0)
            return;

        var message = "SIA_ADMIN_PASSWORD is too weak; require " + string.Join(", ", problems) + ".";
        if (isProduction)
            throw new InvalidOperationException(message);

        logger.LogWarning("{Message}", message);
    }
}
