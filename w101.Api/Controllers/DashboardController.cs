using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IConfiguration _config;

    public DashboardController(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    [HttpGet("summary")]
    [Authorize]
    public async Task<IActionResult> GetDashboardSummary()
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

            // Kullanıcı temel bilgilerini al
            var userStats = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    wins,
                    losses,
                    diamonds,
                    win_streak
                FROM users 
                WHERE id = @UserId", 
                new { UserId = userId });

            if (userStats == null)
                return NotFound(new { message = "Kullanıcı bulunamadı" });

            var totalGames = (userStats.wins ?? 0) + (userStats.losses ?? 0);
            var totalWins = userStats.wins ?? 0;
            var series = userStats.win_streak ?? 0;
            var diamonds = userStats.diamonds ?? 0;

            return Ok(new { 
                success = true,
                message = "Dashboard özeti başarıyla alındı",
                summary = new
                {
                    totalGames = totalGames,
                    totalWins = totalWins,
                    series = series,
                    diamonds = diamonds,
                    winRate = totalGames > 0 ? Math.Round((double)totalWins / totalGames * 100, 1) : 0.0
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Dashboard özeti alınırken bir hata oluştu", error = ex.Message });
        }
    }
} 