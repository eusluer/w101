using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;
using System.ComponentModel.DataAnnotations;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/messages")]
public class MessagesController : ControllerBase
{
    private readonly IConfiguration _config;

    public MessagesController(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public class SendMessageRequest
    {
        [Required]
        public int ReceiverUserId { get; set; }
        
        [Required]
        [StringLength(1000, MinimumLength = 1)]
        public string Content { get; set; } = string.Empty;
    }

    [HttpPost("send")]
    [Authorize]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest req)
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

            // Kendi kendine mesaj gönderemez
            if (userId == req.ReceiverUserId)
                return BadRequest(new { message = "Kendinize mesaj gönderemezsiniz" });

            // Alıcı kullanıcı var mı kontrol et
            var receiver = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id, username FROM users WHERE id = @UserId", 
                new { UserId = req.ReceiverUserId });
            
            if (receiver == null)
                return NotFound(new { message = "Alıcı kullanıcı bulunamadı" });

            // Arkadaş mı kontrol et
            var friendship = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT id FROM friendships 
                WHERE user_id = @UserId AND friend_id = @FriendId", 
                new { UserId = userId, FriendId = req.ReceiverUserId });

            if (friendship == null)
                return BadRequest(new { message = "Sadece arkadaşlarınıza mesaj gönderebilirsiniz" });

            // Mesajı kaydet
            var messageId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO messages (sender_id, receiver_id, content, created_at, is_read) 
                VALUES (@SenderId, @ReceiverId, @Content, NOW(), false) 
                RETURNING id", 
                new { 
                    SenderId = userId, 
                    ReceiverId = req.ReceiverUserId, 
                    Content = req.Content 
                });

            return Ok(new { 
                success = true, 
                message = "Mesaj başarıyla gönderildi",
                messageId = messageId
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Mesaj gönderilirken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetMessageHistory([FromQuery] int friendId)
    {
        try
        {
            if (friendId <= 0)
                return BadRequest(new { message = "Geçerli bir friend_id parametresi gerekli" });

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || string.IsNullOrEmpty(userIdClaim.Value))
                return Unauthorized(new { message = "Geçersiz token" });

            if (!int.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(new { message = "Geçersiz kullanıcı ID" });

            var connectionString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
                return StatusCode(500, new { message = "Veritabanı bağlantı yapılandırması bulunamadı" });

            using var conn = new NpgsqlConnection(connectionString);

            // Arkadaş mı kontrol et
            var friendship = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT id FROM friendships 
                WHERE user_id = @UserId AND friend_id = @FriendId", 
                new { UserId = userId, FriendId = friendId });

            if (friendship == null)
                return BadRequest(new { message = "Bu kullanıcı arkadaşınız değil" });

            // Mesaj geçmişini al
            var sql = @"
                SELECT 
                    m.id,
                    m.sender_id,
                    m.content,
                    m.created_at,
                    m.is_read
                FROM messages m
                WHERE (m.sender_id = @UserId AND m.receiver_id = @FriendId)
                   OR (m.sender_id = @FriendId AND m.receiver_id = @UserId)
                ORDER BY m.created_at ASC
                LIMIT 100";

            var messages = await conn.QueryAsync<dynamic>(sql, new { UserId = userId, FriendId = friendId });

            if (messages == null)
                return Ok(new { 
                    success = true, 
                    message = "Hiç mesaj bulunamadı",
                    messages = new List<object>() 
                });

            // Okunmamış mesajları okundu olarak işaretle
            await conn.ExecuteAsync(@"
                UPDATE messages 
                SET is_read = true 
                WHERE receiver_id = @UserId AND sender_id = @FriendId AND is_read = false", 
                new { UserId = userId, FriendId = friendId });

            var messageHistory = messages.Select(m => new
            {
                id = m?.id ?? 0,
                senderId = m?.sender_id ?? 0,
                content = m?.content?.ToString() ?? "",
                createdAt = m?.created_at,
                isRead = m?.is_read ?? false
            });

            return Ok(new { 
                success = true, 
                message = $"{messageHistory.Count()} mesaj bulundu",
                messages = messageHistory 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Mesaj geçmişi alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("unread-count")]
    [Authorize]
    public async Task<IActionResult> GetUnreadMessageCount()
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
            
            var unreadCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM messages WHERE receiver_id = @UserId AND is_read = false", 
                new { UserId = userId });

            return Ok(new { 
                success = true, 
                unreadCount = unreadCount
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Okunmamış mesaj sayısı alınırken bir hata oluştu", error = ex.Message });
        }
    }
} 