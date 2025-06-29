using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using w101.Api.DTOs;
using w101.Api.Services;

namespace w101.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly ProfileService _profileService;

        public ProfileController(ProfileService profileService)
        {
            _profileService = profileService;
        }

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null)
                {
                    return Unauthorized(new { message = "Geçersiz token" });
                }

                var userId = int.Parse(userIdClaim.Value);
                var profile = await _profileService.GetProfileAsync(userId);

                if (profile == null)
                {
                    return NotFound(new { message = "Kullanıcı bulunamadı" });
                }

                return Ok(new 
                { 
                    success = true,
                    message = "Profil bilgileri başarıyla getirildi",
                    data = profile
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Profil bilgileri alınırken bir hata oluştu", error = ex.Message });
            }
        }

        [HttpPut]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null)
                {
                    return Unauthorized(new { message = "Geçersiz token" });
                }

                var userId = int.Parse(userIdClaim.Value);
                var success = await _profileService.UpdateProfileAsync(userId, request);

                if (!success)
                {
                    return BadRequest(new { message = "Profil güncellenemedi" });
                }

                // Güncellenmiş profil bilgilerini getir
                var updatedProfile = await _profileService.GetProfileAsync(userId);

                return Ok(new 
                { 
                    success = true,
                    message = "Profil başarıyla güncellendi",
                    data = updatedProfile
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Profil güncellenirken bir hata oluştu", error = ex.Message });
            }
        }
    }
} 