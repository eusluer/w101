using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;
using System.ComponentModel.DataAnnotations;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IConfiguration _config;

    public ProfileController(IConfiguration config)
    {
        _config = config;
    }

    public class UpdateProfileRequest
    {
        [StringLength(255, ErrorMessage = "Görünen ad 255 karakterden fazla olamaz")]
        public string? DisplayName { get; set; }

        [StringLength(500, ErrorMessage = "Avatar URL 500 karakterden fazla olamaz")]
        public string? AvatarUrl { get; set; }

        [StringLength(100, ErrorMessage = "Ad 100 karakterden fazla olamaz")]
        public string? FirstName { get; set; }

        [StringLength(100, ErrorMessage = "Soyad 100 karakterden fazla olamaz")]
        public string? LastName { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
                return Unauthorized(new { message = "Geçersiz token" });

            var userId = int.Parse(userIdClaim.Value);

            using var conn = new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));
            
            // Kullanıcı bilgilerini ve rütbe bilgilerini getir
            var sql = @"
                SELECT 
                    u.id,
                    u.username,
                    u.email,
                    u.display_name,
                    u.avatar_url,
                    u.rank_id,
                    u.level,
                    u.diamonds,
                    u.wins,
                    u.losses,
                    u.last_login,
                    u.created_at,
                    u.language,
                    r.name as rank_name,
                    r.min_level as rank_min_level,
                    r.min_diamonds as rank_min_diamonds
                FROM users u
                LEFT JOIN ranks r ON u.rank_id = r.id
                WHERE u.id = @UserId";

            var user = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { UserId = userId });
            
            if (user == null)
                return NotFound(new { message = "Kullanıcı bulunamadı" });

            // İstatistikleri hesapla
            var totalMatches = user.wins + user.losses;
            var winRate = totalMatches > 0 ? Math.Round((double)user.wins / totalMatches * 100, 2) : 0.0;

            var profileData = new
            {
                id = user.id,
                username = user.username,
                email = user.email,
                displayName = user.display_name,
                avatarUrl = user.avatar_url,
                level = user.level,
                diamonds = user.diamonds,
                rank = new
                {
                    id = user.rank_id,
                    name = user.rank_name,
                    minLevel = user.rank_min_level,
                    minDiamonds = user.rank_min_diamonds
                },
                statistics = new
                {
                    wins = user.wins,
                    losses = user.losses,
                    totalMatches = totalMatches,
                    winRate = winRate
                },
                lastLogin = user.last_login,
                createdAt = user.created_at,
                language = user.language
            };

            return Ok(new { success = true, profile = profileData });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Profil bilgileri alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
                return Unauthorized(new { message = "Geçersiz token" });

            var userId = int.Parse(userIdClaim.Value);

            using var conn = new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));
            
            // Güncelleme SQL'ini dinamik olarak oluştur
            var updateFields = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("UserId", userId);

            if (!string.IsNullOrEmpty(req.DisplayName))
            {
                updateFields.Add("display_name = @DisplayName");
                parameters.Add("DisplayName", req.DisplayName);
            }

            if (!string.IsNullOrEmpty(req.AvatarUrl))
            {
                updateFields.Add("avatar_url = @AvatarUrl");
                parameters.Add("AvatarUrl", req.AvatarUrl);
            }

            // Not: Veritabanı şemasında first_name ve last_name kolonları görünmediği için
            // bu alanları users tablosuna eklemek gerekebilir veya user_settings tablosunda tutulabilir

            if (updateFields.Count == 0)
                return BadRequest(new { message = "Güncellenecek alan bulunamadı" });

            updateFields.Add("updated_at = NOW()");

            var updateSql = $"UPDATE users SET {string.Join(", ", updateFields)} WHERE id = @UserId";
            
            await conn.ExecuteAsync(updateSql, parameters);

            // Güncellenmiş profil bilgilerini getir
            var updatedProfile = await GetUpdatedProfile(conn, userId);

            return Ok(new { 
                success = true, 
                message = "Profil başarıyla güncellendi",
                profile = updatedProfile 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Profil güncellenirken bir hata oluştu", error = ex.Message });
        }
    }

    private async Task<object> GetUpdatedProfile(NpgsqlConnection conn, int userId)
    {
        var sql = @"
            SELECT 
                u.id,
                u.username,
                u.email,
                u.display_name,
                u.avatar_url,
                u.rank_id,
                u.level,
                u.diamonds,
                u.wins,
                u.losses,
                u.last_login,
                u.created_at,
                u.updated_at,
                u.language,
                r.name as rank_name,
                r.min_level as rank_min_level,
                r.min_diamonds as rank_min_diamonds
            FROM users u
            LEFT JOIN ranks r ON u.rank_id = r.id
            WHERE u.id = @UserId";

        var user = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { UserId = userId });
        
        var totalMatches = user.wins + user.losses;
        var winRate = totalMatches > 0 ? Math.Round((double)user.wins / totalMatches * 100, 2) : 0.0;

        return new
        {
            id = user.id,
            username = user.username,
            email = user.email,
            displayName = user.display_name,
            avatarUrl = user.avatar_url,
            level = user.level,
            diamonds = user.diamonds,
            rank = new
            {
                id = user.rank_id,
                name = user.rank_name,
                minLevel = user.rank_min_level,
                minDiamonds = user.rank_min_diamonds
            },
            statistics = new
            {
                wins = user.wins,
                losses = user.losses,
                totalMatches = totalMatches,
                winRate = winRate
            },
            lastLogin = user.last_login,
            createdAt = user.created_at,
            updatedAt = user.updated_at,
            language = user.language
        };
    }
} 