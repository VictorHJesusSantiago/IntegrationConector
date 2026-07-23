using System.Globalization;
using IntegrationConnector.Api.Dtos;
using IntegrationConnector.Api.Security;
using IntegrationConnector.Core.Entities;
using IntegrationConnector.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IntegrationConnector.Api.Controllers;

/// <summary>Autenticação local via usuário/senha + JWT (sem IdP externo) e administração de usuários/papéis.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly JwtTokenService _tokenService;
    private readonly PasswordHasher<User> _passwordHasher = new();
    private readonly IConfiguration _configuration;

    public AuthController(IUserRepository userRepository, JwtTokenService tokenService, IConfiguration configuration)
    {
        _userRepository = userRepository;
        _tokenService = tokenService;
        _configuration = configuration;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username, ct);
        if (user is null || !user.IsActive)
            return Problem(detail: "Usuário ou senha inválidos.", statusCode: StatusCodes.Status401Unauthorized, title: "Credenciais inválidas");

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            return Problem(detail: "Usuário ou senha inválidos.", statusCode: StatusCodes.Status401Unauthorized, title: "Credenciais inválidas");

        var token = _tokenService.GenerateToken(user);
        var hours = int.Parse(_configuration["Jwt:ExpirationHours"] ?? "8", CultureInfo.InvariantCulture);
        return Ok(new LoginResponse(token, user.Username, user.Role, DateTime.UtcNow.AddHours(hours)));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("users")]
    public async Task<ActionResult<User>> CreateUser(CreateUserRequest request, CancellationToken ct)
    {
        if (await _userRepository.GetByUsernameAsync(request.Username, ct) is not null)
            return Problem(detail: "Já existe um usuário com esse username.", statusCode: StatusCodes.Status409Conflict, title: "Usuário duplicado");

        var user = new User { Username = request.Username, Role = request.Role };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        await _userRepository.AddAsync(user, ct);
        await _userRepository.SaveChangesAsync(ct);
        return Ok(new { user.Id, user.Username, user.Role });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
    {
        var users = await _userRepository.GetAllAsync(ct);
        return Ok(users.Select(u => new { u.Id, u.Username, u.Role, u.IsActive, u.CreatedAt }));
    }
}
