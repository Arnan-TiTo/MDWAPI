namespace MDWAPI.DTOs;

public record LoginRequest(string Username, string Password);
public record TokenResponse(string Token, DateTime ExpiresAtUtc);
public record RevokeTokenRequest(string Token);
