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

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _authService.RegisterAsync(request);
            
            if (result == null)
            {
                return BadRequest(new { message = "Kullanıcı adı veya e-posta zaten kullanımda" });
            }

            return Ok(new 
            { 
                success = true,
                message = "Kayıt başarılı",
                data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Kayıt sırasında bir hata oluştu", error = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _authService.LoginAsync(request);
            
            if (result == null)
            {
                return Unauthorized(new { message = "Kullanıcı adı/e-posta veya şifre hatalı" });
            }

            return Ok(new 
            { 
                success = true,
                message = "Giriş başarılı",
                data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Giriş sırasında bir hata oluştu", error = ex.Message });
        }
    }

    }
} 