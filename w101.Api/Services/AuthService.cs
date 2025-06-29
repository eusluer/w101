using Dapper;
using Npgsql;
using BCrypt.Net;
using w101.Api.DTOs;

namespace w101.Api.Services
{
    public class AuthService
    {
        private readonly IConfiguration _configuration;
        private readonly JwtService _jwtService;

        public AuthService(IConfiguration configuration, JwtService jwtService)
        {
            _configuration = configuration;
            _jwtService = jwtService;
        }

        private string GetConnectionString()
        {
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
            if (!string.IsNullOrEmpty(databaseUrl))
            {
                // Convert PostgreSQL URI to Npgsql connection string
                var uri = new Uri(databaseUrl);
                var userInfo = uri.UserInfo.Split(':');
                var username = userInfo[0]; // Use the full username from URI
                return $"Host={uri.Host};Port={uri.Port};Database={uri.LocalPath.Substring(1)};Username={username};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
            }
            
            return _configuration.GetConnectionString("DefaultConnection")!;
        }

        public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
        {
            using var connection = new NpgsqlConnection(GetConnectionString());
            
            // Check if user already exists
            var existingUser = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id FROM users WHERE username = @Username OR email = @Email",
                new { request.Username, request.Email }
            );

            if (existingUser != null)
            {
                return null; // User already exists
            }

            // Hash password
            var passwordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(request.Password);

            // Insert new user
            var userId = await connection.QuerySingleAsync<int>(@"
                INSERT INTO users (username, email, password_hash, display_name, level, diamonds, wins, losses, created_at, updated_at, language)
                VALUES (@Username, @Email, @PasswordHash, @Username, 1, 100, 0, 0, @Now, @Now, 'tr')
                RETURNING id",
                new 
                { 
                    request.Username, 
                    request.Email, 
                    PasswordHash = passwordHash,
                    Now = DateTime.UtcNow
                }
            );

            // Generate JWT token
            var token = _jwtService.GenerateToken(userId, request.Username, request.Email);

            return new AuthResponse
            {
                Token = token,
                UserId = userId,
                Username = request.Username,
                Email = request.Email
            };
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            using var connection = new NpgsqlConnection(GetConnectionString());
            
            // Get user by username or email
            var user = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT id, username, email, password_hash 
                FROM users 
                WHERE username = @UsernameOrEmail OR email = @UsernameOrEmail",
                new { UsernameOrEmail = request.UsernameOrEmail }
            );

            if (user == null)
            {
                return null; // User not found
            }

            // Verify password
            if (!BCrypt.Net.BCrypt.EnhancedVerify(request.Password, user.password_hash))
            {
                return null; // Invalid password
            }

            // Update last login
            await connection.ExecuteAsync(
                "UPDATE users SET last_login = @Now WHERE id = @UserId",
                new { Now = DateTime.UtcNow, UserId = user.id }
            );

            // Generate JWT token
            var token = _jwtService.GenerateToken(user.id, user.username, user.email);

            return new AuthResponse
            {
                Token = token,
                UserId = user.id,
                Username = user.username,
                Email = user.email
            };
        }
    }
} 