using System.Globalization;
using System.Text;
using SiaWebhookApi.Models;

namespace SiaWebhookApi.Services;

/// <summary>
/// Validates the inbound Zepcam webhook payload before any of its fields are
/// framed into a SIA DC-09 message. Identity fields travel verbatim on the wire
/// in the unencrypted path, so anything that could break SIA framing (delimiters,
/// control characters, oversized fields) is rejected outright — fail loud, never
/// silently strip, for an alarm system.
/// </summary>
public static class ZepcamWebhookValidator
{
    public const int MaxIdentityFieldLength = 64;
    public const int MaxSiaPayloadBytes = 4095;

    private static readonly char[] DelimitersAndControls = ['\r', '\n', '[', ']', '|', '_'];

    public static (bool IsValid, string? Error) Validate(DataTransferObject data)
    {
        if (data.Location is null || string.IsNullOrWhiteSpace(data.Location.Latitude) || string.IsNullOrWhiteSpace(data.Location.Longitude))
            return (false, "Missing or incomplete 'location' field (latitude/longitude required).");

        if (!TryParseCoordinate(data.Location.Latitude, -90, 90))
            return (false, "'location.latitude' must be a numeric value between -90 and 90.");

        if (!TryParseCoordinate(data.Location.Longitude, -180, 180))
            return (false, "'location.longitude' must be a numeric value between -180 and 180.");

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

    /// <summary>
    /// Guards the DC-09 length-field ceiling: the frame length is encoded as
    /// 3 hex digits, so a payload over 4095 bytes would overflow it and corrupt
    /// framing. Field caps make this unreachable in practice; this is the backstop.
    /// </summary>
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
