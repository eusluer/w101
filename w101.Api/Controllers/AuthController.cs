using Microsoft.AspNetCore.Mvc;
using w101.Api.DTOs;
using w101.Api.Services;

namespace w101.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(AuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("Register endpoint called for username: {Username}", request?.Username ?? "null");

            if (!ModelState.IsValid || request == null)
            {
                _logger.LogWarning("Register request validation failed");
                return BadRequest(ModelState);
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30 saniye timeout
                
                var result = await _authService.RegisterAsync(request);
                
                if (result == null)
                {
                    var duration = DateTime.UtcNow - startTime;
                    _logger.LogWarning("Registration failed - user exists. Duration: {Duration}ms", duration.TotalMilliseconds);
                    return BadRequest(new { message = "Kullanıcı adı veya e-posta zaten kullanımda" });
                }

                var successDuration = DateTime.UtcNow - startTime;
                _logger.LogInformation("Registration successful. Duration: {Duration}ms", successDuration.TotalMilliseconds);

                return Ok(new 
                { 
                    success = true,
                    message = "Kayıt başarılı",
                    data = result
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Registration timeout for username: {Username}", request?.Username ?? "null");
                return StatusCode(408, new { message = "İstek zaman aşımına uğradı, lütfen tekrar deneyin" });
            }
            catch (Exception ex)
            {
                var errorDuration = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "Registration error for username: {Username}. Duration: {Duration}ms", request?.Username ?? "null", errorDuration.TotalMilliseconds);
                return StatusCode(500, new { message = "Kayıt sırasında bir hata oluştu, lütfen tekrar deneyin", error = ex.Message });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var startTime = DateTime.UtcNow;
            _logger.LogInformation("Login endpoint called for: {UsernameOrEmail}", request?.UsernameOrEmail ?? "null");

            if (!ModelState.IsValid || request == null)
            {
                _logger.LogWarning("Login request validation failed");
                return BadRequest(ModelState);
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30 saniye timeout
                
                var result = await _authService.LoginAsync(request);
                
                if (result == null)
                {
                    var duration = DateTime.UtcNow - startTime;
                    _logger.LogWarning("Login failed - invalid credentials. Duration: {Duration}ms", duration.TotalMilliseconds);
                    return Unauthorized(new { message = "Kullanıcı adı/e-posta veya şifre hatalı" });
                }

                var successDuration = DateTime.UtcNow - startTime;
                _logger.LogInformation("Login successful. Duration: {Duration}ms", successDuration.TotalMilliseconds);

                return Ok(new 
                { 
                    success = true,
                    message = "Giriş başarılı",
                    data = result
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Login timeout for: {UsernameOrEmail}", request?.UsernameOrEmail ?? "null");
                return StatusCode(408, new { message = "İstek zaman aşımına uğradı, lütfen tekrar deneyin" });
            }
            catch (Exception ex)
            {
                var errorDuration = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "Login error for: {UsernameOrEmail}. Duration: {Duration}ms", request?.UsernameOrEmail ?? "null", errorDuration.TotalMilliseconds);
                return StatusCode(500, new { message = "Giriş sırasında bir hata oluştu, lütfen tekrar deneyin", error = ex.Message });
            }
        }
    }
} 