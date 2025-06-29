using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Dapper;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    public AuthController(IConfiguration config)
    {
        _config = config;
    }

    public class RegisterRequest
    {
        [Required]
        public string Username { get; set; } = string.Empty;
        [Required]
        public string Password { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public string? Language { get; set; }
    }

    public class LoginRequest
    {
        [Required]
        public string Username { get; set; } = string.Empty;
        [Required]
        public string Password { get; set; } = string.Empty;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        using var conn = new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));
        var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users WHERE username=@Username", new { req.Username });
        if (exists > 0)
            return BadRequest(new { message = "Kullanıcı adı zaten mevcut" });

        string hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        var sql = @"INSERT INTO users (username, password_hash, email, display_name, language, created_at) VALUES (@Username, @PasswordHash, @Email, @DisplayName, @Language, NOW()) RETURNING id;";
        var userId = await conn.ExecuteScalarAsync<int>(sql, new { Username = req.Username, PasswordHash = hash, Email = req.Email, DisplayName = req.DisplayName, Language = req.Language });
        return Ok(new { success = true, userId });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        using var conn = new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));
        var user = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT * FROM users WHERE username=@Username", new { req.Username });
        if (user == null)
            return Unauthorized(new { message = "Kullanıcı bulunamadı" });
        if (!BCrypt.Net.BCrypt.Verify(req.Password, (string)user.password_hash))
            return Unauthorized(new { message = "Şifre hatalı" });

        // JWT Token üret
        var token = GenerateJwtToken(user);

        // Son giriş zamanını güncelle
        await conn.ExecuteAsync("UPDATE users SET last_login = NOW(), updated_at = NOW() WHERE id = @Id", new { Id = user.id });

        return Ok(new { 
            success = true, 
            token = token,
            user = new { 
                user.id, 
                user.username, 
                user.email, 
                user.display_name,
                user.level,
                user.diamonds
            }
        });
    }

    private string GenerateJwtToken(dynamic user)
    {
        var jwtSettings = _config.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"];
        var issuer = jwtSettings["Issuer"];
        var audience = jwtSettings["Audience"];
        var expiryInMinutes = int.Parse(jwtSettings["ExpiryInMinutes"] ?? "60");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
            new Claim(ClaimTypes.Name, user.username),
            new Claim("email", user.email ?? ""),
            new Claim("display_name", user.display_name ?? ""),
            new Claim("level", user.level.ToString()),
            new Claim("diamonds", user.diamonds.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryInMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
} 