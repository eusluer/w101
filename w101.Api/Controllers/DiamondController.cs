using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/diamonds")]
public class DiamondController : ControllerBase
{
    private readonly IConfiguration _config;

    public DiamondController(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetDiamondHistory()
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
            
            var sql = @"
                SELECT 
                    id,
                    transaction_type as type,
                    amount,
                    description,
                    created_at
                FROM diamond_transactions 
                WHERE user_id = @UserId
                ORDER BY created_at DESC
                LIMIT 50";

            var transactions = await conn.QueryAsync<dynamic>(sql, new { UserId = userId });

            if (transactions == null)
                return Ok(new { 
                    success = true, 
                    message = "Hiç elmas işlemi bulunamadı",
                    history = new List<object>() 
                });

            var diamondHistory = transactions.Select(t => new
            {
                id = t?.id ?? 0,
                type = t?.type?.ToString() ?? "unknown",
                amount = t?.amount ?? 0,
                description = t?.description?.ToString() ?? "Açıklama yok",
                createdAt = t?.created_at
            });

            return Ok(new { 
                success = true, 
                message = $"Son {diamondHistory.Count()} elmas işlemi getirildi",
                history = diamondHistory 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Elmas geçmişi alınırken bir hata oluştu", error = ex.Message });
        }
    }
} 