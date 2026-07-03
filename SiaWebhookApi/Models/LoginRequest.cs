namespace SiaWebhookApi.Models;

/// <summary>Body for POST /api/auth/login.</summary>
public record LoginRequest(string? Password);
