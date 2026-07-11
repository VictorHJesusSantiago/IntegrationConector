using IntegrationConnector.Core.Enums;

namespace IntegrationConnector.Api.Dtos;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username, UserRole Role, DateTime ExpiresAtUtc);
public record CreateUserRequest(string Username, string Password, UserRole Role);
