using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/rewards")]
public class RewardsController : ControllerBase
{
    private readonly IConfiguration _config;

    public RewardsController(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    [HttpPost("daily")]
    [Authorize]
    public async Task<IActionResult> ClaimDailyReward()
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

            // Kullanıcının son günlük ödül zamanını kontrol et
            var lastDailyReward = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    last_daily_reward,
                    diamonds 
                FROM users 
                WHERE id = @UserId", 
                new { UserId = userId });

            if (lastDailyReward == null)
                return NotFound(new { message = "Kullanıcı bulunamadı" });

            var now = DateTime.UtcNow;
            var lastRewardTime = lastDailyReward.last_daily_reward as DateTime?;

            // Aynı gün içinde ödül alınmış mı kontrol et
            if (lastRewardTime.HasValue && lastRewardTime.Value.Date == now.Date)
            {
                return BadRequest(new { 
                    success = false,
                    message = "Günlük ödülünüzü bugün zaten aldınız",
                    nextRewardAt = lastRewardTime.Value.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss")
                });
            }

            // Günlük ödül miktarı (sabit veya değişken olabilir)
            var dailyRewardAmount = 50; // 50 elmas

            using var transaction = conn.BeginTransaction();
            try
            {
                // Kullanıcının elmasını artır ve son günlük ödül zamanını güncelle
                await conn.ExecuteAsync(@"
                    UPDATE users 
                    SET diamonds = diamonds + @RewardAmount, 
                        last_daily_reward = @Now 
                    WHERE id = @UserId", 
                    new { 
                        UserId = userId, 
                        RewardAmount = dailyRewardAmount, 
                        Now = now 
                    }, transaction);

                // Diamond transaction kaydı
                await conn.ExecuteAsync(@"
                    INSERT INTO diamond_transactions (user_id, amount, transaction_type, description, created_at) 
                    VALUES (@UserId, @Amount, @Type, @Description, @Now)", 
                    new { 
                        UserId = userId,
                        Amount = dailyRewardAmount,
                        Type = "daily_reward",
                        Description = "Günlük giriş ödülü",
                        Now = now
                    }, transaction);

                transaction.Commit();

                return Ok(new { 
                    success = true, 
                    message = "Günlük ödülünüz başarıyla alındı",
                    reward = dailyRewardAmount,
                    newDiamondTotal = (lastDailyReward.diamonds ?? 0) + dailyRewardAmount
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Günlük ödül alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpPost("ad")]
    [Authorize]
    public async Task<IActionResult> ClaimAdReward()
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

            // Kullanıcının son reklam ödül zamanını kontrol et
            var lastAdReward = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    last_ad_reward,
                    diamonds 
                FROM users 
                WHERE id = @UserId", 
                new { UserId = userId });

            if (lastAdReward == null)
                return NotFound(new { message = "Kullanıcı bulunamadı" });

            var now = DateTime.UtcNow;
            var lastRewardTime = lastAdReward.last_ad_reward as DateTime?;

            // 20 dakika geçmiş mi kontrol et
            if (lastRewardTime.HasValue && now.Subtract(lastRewardTime.Value).TotalMinutes < 20)
            {
                var remainingMinutes = 20 - (int)now.Subtract(lastRewardTime.Value).TotalMinutes;
                return BadRequest(new { 
                    success = false,
                    message = $"Bir sonraki reklam ödülü için {remainingMinutes} dakika beklemelisiniz",
                    nextRewardAt = lastRewardTime.Value.AddMinutes(20).ToString("yyyy-MM-dd HH:mm:ss")
                });
            }

            // Reklam ödül miktarı
            var adRewardAmount = 25; // 25 elmas

            using var transaction = conn.BeginTransaction();
            try
            {
                // Kullanıcının elmasını artır ve son reklam ödül zamanını güncelle
                await conn.ExecuteAsync(@"
                    UPDATE users 
                    SET diamonds = diamonds + @RewardAmount, 
                        last_ad_reward = @Now 
                    WHERE id = @UserId", 
                    new { 
                        UserId = userId, 
                        RewardAmount = adRewardAmount, 
                        Now = now 
                    }, transaction);

                // Diamond transaction kaydı
                await conn.ExecuteAsync(@"
                    INSERT INTO diamond_transactions (user_id, amount, transaction_type, description, created_at) 
                    VALUES (@UserId, @Amount, @Type, @Description, @Now)", 
                    new { 
                        UserId = userId,
                        Amount = adRewardAmount,
                        Type = "ad_reward",
                        Description = "Reklam izleme ödülü",
                        Now = now
                    }, transaction);

                transaction.Commit();

                return Ok(new { 
                    success = true, 
                    message = "Reklam ödülünüz başarıyla alındı",
                    reward = adRewardAmount,
                    newDiamondTotal = (lastAdReward.diamonds ?? 0) + adRewardAmount
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Reklam ödülü alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("status")]
    [Authorize]
    public async Task<IActionResult> GetRewardStatus()
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

            var rewardStatus = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    last_daily_reward,
                    last_ad_reward 
                FROM users 
                WHERE id = @UserId", 
                new { UserId = userId });

            if (rewardStatus == null)
                return NotFound(new { message = "Kullanıcı bulunamadı" });

            var now = DateTime.UtcNow;
            var lastDailyReward = rewardStatus.last_daily_reward as DateTime?;
            var lastAdReward = rewardStatus.last_ad_reward as DateTime?;

            // Günlük ödül durumu
            bool canClaimDaily = !lastDailyReward.HasValue || lastDailyReward.Value.Date < now.Date;
            
            // Reklam ödül durumu (20 dakika)
            bool canClaimAd = !lastAdReward.HasValue || now.Subtract(lastAdReward.Value).TotalMinutes >= 20;

            return Ok(new { 
                success = true,
                canClaimDaily = canClaimDaily,
                canClaimAd = canClaimAd,
                nextDailyReward = lastDailyReward?.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"),
                nextAdReward = lastAdReward?.AddMinutes(20).ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Ödül durumu alınırken bir hata oluştu", error = ex.Message });
        }
    }
} 