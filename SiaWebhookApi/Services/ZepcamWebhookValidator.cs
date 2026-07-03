using System.Globalization;
using System.Text;
using SiaWebhookApi.Models;

namespace SiaWebhookApi.Services;

public static class ZepcamWebhookValidator
{
    public const int MaxIdentityFieldLength = 64;
    public const int MaxSiaPayloadBytes = 4095;
    public static readonly TimeSpan MaxTimestampAge = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    private static readonly char[] DelimitersAndControls = ['\r', '\n', '[', ']', '|', '_'];

    public static (bool IsValid, string? Error) Validate(DataTransferObject data)
    {
        if (data.Location is null || string.IsNullOrWhiteSpace(data.Location.Latitude) || string.IsNullOrWhiteSpace(data.Location.Longitude))
            return (false, "Missing or incomplete 'location' field (latitude/longitude required).");

        if (!TryParseCoordinate(data.Location.Latitude, -90, 90))
            return (false, "'location.latitude' must be a numeric value between -90 and 90.");

        if (!TryParseCoordinate(data.Location.Longitude, -180, 180))
            return (false, "'location.longitude' must be a numeric value between -180 and 180.");

        if (data.Timestamp == default)
            return (false, "Missing or invalid 'timestamp' field.");

        var now = DateTimeOffset.UtcNow;
        if (data.Timestamp > now.Add(MaxFutureSkew))
            return (false, "'timestamp' is too far in the future.");

        if (data.Timestamp < now.Subtract(MaxTimestampAge))
            return (false, "'timestamp' is too old.");

        foreach (var (fieldName, value) in new[]
        {
            ("user_name", data.UserName),
            ("user_externalid", data.UserExternalId),
            ("issuer", data.Issuer)
        })
        {
            var error = ValidateIdentityField(fieldName, value);
            if (error is not null)
                return (false, error);
        }

        return (true, null);
    }

    public static void EnsurePayloadFits(string payload)
    {
        var length = Encoding.ASCII.GetByteCount(payload);
        if (length > MaxSiaPayloadBytes)
            throw new InvalidOperationException($"SIA payload length {length} exceeds the {MaxSiaPayloadBytes}-byte DC-09 length limit.");
    }

    private static bool TryParseCoordinate(string value, double min, double max)
    {
        return double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= min
            && parsed <= max;
    }

    private static string? ValidateIdentityField(string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return $"Missing '{fieldName}' field.";

        if (value.Length > MaxIdentityFieldLength)
            return $"'{fieldName}' must be {MaxIdentityFieldLength} characters or fewer.";

        return value.IndexOfAny(DelimitersAndControls) >= 0
            ? $"'{fieldName}' contains a reserved SIA delimiter/control character."
            : null;
    }
}
