using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;
using System.ComponentModel.DataAnnotations;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/store")]
public class StoreController : ControllerBase
{
    private readonly IConfiguration _config;

    public StoreController(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public class BuyItemRequest
    {
        [Required]
        public int ShopItemId { get; set; }
    }

    [HttpGet("items")]
    public async Task<IActionResult> GetStoreItems()
    {
        try
        {
            var connectionString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
                return StatusCode(500, new { message = "Veritabanı bağlantı yapılandırması bulunamadı" });

            using var conn = new NpgsqlConnection(connectionString);
            
            var sql = @"
                SELECT 
                    id as shop_item_id,
                    name,
                    diamond_amount,
                    price_local
                FROM shop_items 
                WHERE is_active = true
                ORDER BY diamond_amount ASC";

            var items = await conn.QueryAsync<dynamic>(sql);

            if (items == null)
                return Ok(new { 
                    success = true, 
                    message = "Mağaza ürünü bulunamadı",
                    items = new List<object>() 
                });

            var storeItems = items.Select(item => new
            {
                shopItemId = item?.shop_item_id ?? 0,
                name = item?.name?.ToString() ?? "Bilinmeyen",
                diamondAmount = item?.diamond_amount ?? 0,
                priceLocal = item?.price_local ?? 0.0
            });

            return Ok(new { 
                success = true, 
                message = $"{storeItems.Count()} mağaza ürünü bulundu",
                items = storeItems 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Mağaza ürünleri alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpPost("buy")]
    [Authorize]
    public async Task<IActionResult> BuyItem([FromBody] BuyItemRequest req)
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
            
            // Kullanıcı bilgilerini al
            var user = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id, username, diamonds FROM users WHERE id = @UserId", 
                new { UserId = userId });
            
            if (user == null)
                return NotFound(new { message = "Kullanıcı bulunamadı" });

            // Shop item bilgilerini al
            var shopItem = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id, name, diamond_amount, price_local, is_active FROM shop_items WHERE id = @ShopItemId", 
                new { ShopItemId = req.ShopItemId });
            
            if (shopItem == null)
                return NotFound(new { message = "Mağaza ürünü bulunamadı" });

            if (!shopItem.is_active)
                return BadRequest(new { message = "Bu ürün artık satışta değil" });

            using var transaction = conn.BeginTransaction();
            try
            {
                // Kullanıcının elmasını artır
                await conn.ExecuteAsync(@"
                    UPDATE users 
                    SET diamonds = diamonds + @DiamondAmount 
                    WHERE id = @UserId", 
                    new { UserId = userId, DiamondAmount = shopItem.diamond_amount }, transaction);

                // Diamond transaction kaydı
                await conn.ExecuteAsync(@"
                    INSERT INTO diamond_transactions (user_id, amount, transaction_type, description, created_at) 
                    VALUES (@UserId, @Amount, @Type, @Description, NOW())", 
                    new { 
                        UserId = userId,
                        Amount = shopItem.diamond_amount,
                        Type = "purchase",
                        Description = $"Mağaza satın alma - {shopItem.name}"
                    }, transaction);

                // Satın alma kaydı (eğer purchases tablosu varsa)
                var purchaseId = await conn.ExecuteScalarAsync<int?>(@"
                    INSERT INTO purchases (user_id, shop_item_id, amount_paid, diamonds_received, created_at) 
                    VALUES (@UserId, @ShopItemId, @AmountPaid, @DiamondsReceived, NOW()) 
                    RETURNING id", 
                    new { 
                        UserId = userId,
                        ShopItemId = req.ShopItemId,
                        AmountPaid = shopItem.price_local,
                        DiamondsReceived = shopItem.diamond_amount
                    }, transaction);

                transaction.Commit();

                // Güncel elmas miktarını al
                var updatedUser = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT diamonds FROM users WHERE id = @UserId", 
                    new { UserId = userId });

                return Ok(new { 
                    success = true, 
                    message = "Satın alma başarılı",
                    diamonds = updatedUser?.diamonds ?? (user.diamonds + shopItem.diamond_amount),
                    transactionId = purchaseId ?? 0
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
            return StatusCode(500, new { message = "Satın alma işlemi sırasında bir hata oluştu", error = ex.Message });
        }
    }
} 