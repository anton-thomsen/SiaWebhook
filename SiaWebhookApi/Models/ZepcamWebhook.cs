using System.Text.Json.Serialization;

namespace SiaWebhookApi.Models;

public class Location
{
    [JsonPropertyName("latitude")]  public string? Latitude  { get; set; }
    [JsonPropertyName("longitude")] public string? Longitude { get; set; }
}

public class DataTransferObject
{
    [JsonPropertyName("user_externalid")] public string? UserExternalId { get; set; }
    [JsonPropertyName("user_name")]       public string? UserName       { get; set; }
    [JsonPropertyName("timestamp")]       public DateTimeOffset Timestamp { get; set; }
    [JsonPropertyName("issuer")]          public string? Issuer         { get; set; }
    [JsonPropertyName("location")]        public Location? Location     { get; set; }
}
