using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ClaimsApi.Api.Auth;

public static class Roles
{
    public const string Biller = nameof(Biller);
    public const string Reviewer = nameof(Reviewer);
}

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "claims-api";
    public string Audience { get; set; } = "claims-api-clients";
    public int ExpiryMinutes { get; set; } = 30;
}

public class SeedUser
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public record TokenRequest(string Username, string Password);
public record TokenResponse(string AccessToken, DateTime ExpiresAt);

/// <summary>
/// Demo user store seeded from configuration. Passwords are hashed with PBKDF2 at startup and compared
/// in constant time. Swap for ASP.NET Core Identity or an external IdP in a real deployment.
/// </summary>
public class UserStore
{
    private const int Iterations = 100_000;
    private readonly record struct Entry(string Role, byte[] Salt, byte[] Hash);
    private readonly Dictionary<string, Entry> _users = new(StringComparer.OrdinalIgnoreCase);

    public UserStore(IConfiguration config)
    {
        foreach (var user in config.GetSection("Auth:Users").Get<List<SeedUser>>() ?? [])
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            _users[user.Username] = new Entry(user.Role, salt, Hash(user.Password, salt));
        }
    }

    /// <summary>Returns the user's role when the credentials are valid, otherwise null.</summary>
    public string? Validate(string username, string password)
    {
        if (!_users.TryGetValue(username, out var entry))
        {
            // Burn comparable time so unknown users are not distinguishable by timing.
            Hash(password, new byte[16]);
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(Hash(password, entry.Salt), entry.Hash) ? entry.Role : null;
    }

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
}

public class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public TokenResponse Create(string username, string role)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, username), new Claim(ClaimTypes.Name, username), new Claim(ClaimTypes.Role, role)],
            expires: expires,
            signingCredentials: credentials);

        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

[ApiController]
[Route("api/auth")]
public class AuthController(UserStore users, TokenService tokens) : ControllerBase
{
    /// <summary>Exchanges credentials for a short-lived JWT.</summary>
    [HttpPost("token")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Token([FromBody] TokenRequest request)
    {
        var role = users.Validate(request.Username ?? string.Empty, request.Password ?? string.Empty);
        return role is null ? Unauthorized() : Ok(tokens.Create(request.Username!, role));
    }
}
