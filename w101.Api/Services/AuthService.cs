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
        private readonly string _connectionString;
        private readonly ILogger<AuthService> _logger;

        public AuthService(IConfiguration configuration, JwtService jwtService, string connectionString, ILogger<AuthService> logger)
        {
            _configuration = configuration;
            _jwtService = jwtService;
            _connectionString = connectionString;
            _logger = logger;
        }

        private async Task<NpgsqlConnection> GetConnectionAsync()
        {
            var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
        {
            _logger.LogInformation("Register attempt for username: {Username}, email: {Email}", request.Username, request.Email);
            
            try
            {
                using var connection = await GetConnectionAsync();
                
                // Check if user already exists
                _logger.LogDebug("Checking if user exists");
                var existingUser = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT id FROM users WHERE username = @Username OR email = @Email",
                    new { request.Username, request.Email },
                    commandTimeout: 10 // 10 saniye timeout
                );

                if (existingUser != null)
                {
                    _logger.LogWarning("Registration failed - user already exists: {Username}", request.Username);
                    return null; // User already exists
                }

                // Hash password
                _logger.LogDebug("Hashing password");
                var passwordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(request.Password, 12); // Güvenlik için 12 round

                // Insert new user
                _logger.LogDebug("Creating new user");
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
                    },
                    commandTimeout: 15 // 15 saniye timeout
                );

                // Generate JWT token
                _logger.LogDebug("Generating JWT token for user: {UserId}", userId);
                var token = _jwtService.GenerateToken(userId, request.Username, request.Email);

                _logger.LogInformation("Registration successful for user: {Username}, ID: {UserId}", request.Username, userId);

                return new AuthResponse
                {
                    Token = token,
                    UserId = userId,
                    Username = request.Username,
                    Email = request.Email
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration failed for username: {Username}", request.Username);
                throw;
            }
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            _logger.LogInformation("Login attempt for: {UsernameOrEmail}", request.UsernameOrEmail);
            
            try
            {
                using var connection = await GetConnectionAsync();
                
                // Get user by username or email
                _logger.LogDebug("Fetching user data");
                var user = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT id, username, email, password_hash, last_login, created_at
                    FROM users 
                    WHERE username = @UsernameOrEmail OR email = @UsernameOrEmail",
                    new { UsernameOrEmail = request.UsernameOrEmail },
                    commandTimeout: 10 // 10 saniye timeout
                );

                if (user == null)
                {
                    _logger.LogWarning("Login failed - user not found: {UsernameOrEmail}", request.UsernameOrEmail);
                    return null; // User not found
                }

                _logger.LogDebug($"User found: {user.username}, ID: {user.id}");

                // Verify password
                _logger.LogDebug("Verifying password");
                if (!BCrypt.Net.BCrypt.EnhancedVerify(request.Password, user.password_hash))
                {
                    _logger.LogWarning($"Login failed - invalid password for user: {user.username}");
                    return null; // Invalid password
                }

                // Update last login
                _logger.LogDebug("Updating last login time");
                await connection.ExecuteAsync(
                    "UPDATE users SET last_login = @Now WHERE id = @UserId",
                    new { Now = DateTime.UtcNow, UserId = user.id },
                    commandTimeout: 10 // 10 saniye timeout
                );

                // Generate JWT token
                _logger.LogDebug("Generating JWT token");
                var token = _jwtService.GenerateToken(user.id, user.username, user.email);

                _logger.LogInformation($"Login successful for user: {user.username}, ID: {user.id}");

                return new AuthResponse
                {
                    Token = token,
                    UserId = user.id,
                    Username = user.username,
                    Email = user.email
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed for: {UsernameOrEmail}", request.UsernameOrEmail);
                throw;
            }
        }
    }
} 