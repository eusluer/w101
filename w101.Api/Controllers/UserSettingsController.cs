using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;
using System.ComponentModel.DataAnnotations;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/user-settings")]
public class UserSettingsController : ControllerBase
{
    private readonly IConfiguration _config;

    public UserSettingsController(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public class UserSettingsModel
    {
        public bool SoundEnabled { get; set; } = true;
        public bool MusicEnabled { get; set; } = true;
        public bool NotificationEnabled { get; set; } = true;
        public string Language { get; set; } = "tr";
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetUserSettings()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || string.IsNullOrEmpty(userIdClaim.Value))
                return Unauthorized(new { message = "Geçersiz token" });

            if (!int.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(new { message = "Geçersiz kullanıcı ID" });

            var connectionString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
                return StatusCode(500, new { message = "Veritabanı bağlantı yapılandırması bulunamadı" });

            using var conn = new NpgsqlConnection(connectionString);

            // Kullanıcı ayarlarını al, yoksa varsayılan değerleri döndür
            var settings = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    sound_enabled,
                    music_enabled,
                    notification_enabled,
                    language
                FROM user_settings 
                WHERE user_id = @UserId", 
                new { UserId = userId });

            var userSettings = new UserSettingsModel
            {
                SoundEnabled = settings?.sound_enabled ?? true,
                MusicEnabled = settings?.music_enabled ?? true,
                NotificationEnabled = settings?.notification_enabled ?? true,
                Language = settings?.language?.ToString() ?? "tr"
            };

            return Ok(new { 
                success = true,
                message = "Kullanıcı ayarları başarıyla alındı",
                settings = userSettings
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Kullanıcı ayarları alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpPut]
    [Authorize]
    public async Task<IActionResult> UpdateUserSettings([FromBody] UserSettingsModel settings)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || string.IsNullOrEmpty(userIdClaim.Value))
                return Unauthorized(new { message = "Geçersiz token" });

            if (!int.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(new { message = "Geçersiz kullanıcı ID" });

            var connectionString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
                return StatusCode(500, new { message = "Veritabanı bağlantı yapılandırması bulunamadı" });

            using var conn = new NpgsqlConnection(connectionString);

            // Mevcut ayar var mı kontrol et
            var existingSettings = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id FROM user_settings WHERE user_id = @UserId", 
                new { UserId = userId });

            if (existingSettings == null)
            {
                // Yeni ayar kaydı oluştur
                await conn.ExecuteAsync(@"
                    INSERT INTO user_settings (user_id, sound_enabled, music_enabled, notification_enabled, language, created_at, updated_at) 
                    VALUES (@UserId, @SoundEnabled, @MusicEnabled, @NotificationEnabled, @Language, NOW(), NOW())", 
                    new { 
                        UserId = userId,
                        SoundEnabled = settings.SoundEnabled,
                        MusicEnabled = settings.MusicEnabled,
                        NotificationEnabled = settings.NotificationEnabled,
                        Language = settings.Language
                    });
            }
            else
            {
                // Mevcut ayarları güncelle
                await conn.ExecuteAsync(@"
                    UPDATE user_settings 
                    SET sound_enabled = @SoundEnabled, 
                        music_enabled = @MusicEnabled, 
                        notification_enabled = @NotificationEnabled, 
                        language = @Language,
                        updated_at = NOW()
                    WHERE user_id = @UserId", 
                    new { 
                        UserId = userId,
                        SoundEnabled = settings.SoundEnabled,
                        MusicEnabled = settings.MusicEnabled,
                        NotificationEnabled = settings.NotificationEnabled,
                        Language = settings.Language
                    });
            }

            return Ok(new { 
                success = true, 
                message = "Kullanıcı ayarları başarıyla güncellendi",
                settings = settings
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Kullanıcı ayarları güncellenirken bir hata oluştu", error = ex.Message });
        }
    }
} 