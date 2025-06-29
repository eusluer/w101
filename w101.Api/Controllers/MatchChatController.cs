using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;
using System.ComponentModel.DataAnnotations;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/match-chat")]
public class MatchChatController : ControllerBase
{
    private readonly IConfiguration _config;

    public MatchChatController(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public class SendChatMessageRequest
    {
        [Required]
        public int ChatId { get; set; }
        
        [Required]
        [StringLength(500, MinimumLength = 1)]
        public string Content { get; set; } = string.Empty;
    }

    [HttpPost("send")]
    [Authorize]
    public async Task<IActionResult> SendChatMessage([FromBody] SendChatMessageRequest req)
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

            // Chat'e katılım kontrolü - kullanıcı bu maçta var mı?
            var chatAccess = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    mc.id,
                    mc.match_id,
                    m.status
                FROM match_chats mc
                JOIN matches m ON mc.match_id = m.id
                JOIN match_players mp ON m.id = mp.match_id
                WHERE mc.id = @ChatId AND mp.user_id = @UserId", 
                new { ChatId = req.ChatId, UserId = userId });

            if (chatAccess == null)
                return BadRequest(new { message = "Bu sohbete mesaj gönderme yetkiniz yok" });

            // Maç aktif mi kontrolü
            if (chatAccess.status != "active" && chatAccess.status != "finished")
                return BadRequest(new { message = "Bu maç sohbeti artık aktif değil" });

            // Mesajı kaydet
            var messageId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO match_chat_messages (chat_id, sender_id, content, created_at) 
                VALUES (@ChatId, @SenderId, @Content, NOW()) 
                RETURNING id", 
                new { 
                    ChatId = req.ChatId, 
                    SenderId = userId, 
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
            return StatusCode(500, new { message = "Sohbet mesajı gönderilirken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetChatHistory([FromQuery] int chatId)
    {
        try
        {
            if (chatId <= 0)
                return BadRequest(new { message = "Geçerli bir chat_id parametresi gerekli" });

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || string.IsNullOrEmpty(userIdClaim.Value))
                return Unauthorized(new { message = "Geçersiz token" });

            if (!int.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(new { message = "Geçersiz kullanıcı ID" });

            var connectionString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
                return StatusCode(500, new { message = "Veritabanı bağlantı yapılandırması bulunamadı" });

            using var conn = new NpgsqlConnection(connectionString);

            // Chat'e erişim kontrolü
            var chatAccess = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    mc.id,
                    mc.match_id
                FROM match_chats mc
                JOIN matches m ON mc.match_id = m.id
                JOIN match_players mp ON m.id = mp.match_id
                WHERE mc.id = @ChatId AND mp.user_id = @UserId", 
                new { ChatId = chatId, UserId = userId });

            if (chatAccess == null)
                return BadRequest(new { message = "Bu sohbete erişim yetkiniz yok" });

            // Sohbet geçmişini al
            var sql = @"
                SELECT 
                    mcm.id,
                    mcm.sender_id,
                    u.display_name as sender_name,
                    mcm.content,
                    mcm.created_at
                FROM match_chat_messages mcm
                JOIN users u ON mcm.sender_id = u.id
                WHERE mcm.chat_id = @ChatId
                ORDER BY mcm.created_at ASC
                LIMIT 100";

            var messages = await conn.QueryAsync<dynamic>(sql, new { ChatId = chatId });

            if (messages == null)
                return Ok(new { 
                    success = true, 
                    message = "Hiç sohbet mesajı bulunamadı",
                    messages = new List<object>() 
                });

            var chatHistory = messages.Select(m => new
            {
                id = m?.id ?? 0,
                senderId = m?.sender_id ?? 0,
                senderName = m?.sender_name?.ToString() ?? "Bilinmeyen",
                content = m?.content?.ToString() ?? "",
                createdAt = m?.created_at
            });

            return Ok(new { 
                success = true, 
                message = $"{chatHistory.Count()} sohbet mesajı bulundu",
                chatId = chatId,
                messages = chatHistory 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Sohbet geçmişi alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("active")]
    [Authorize]
    public async Task<IActionResult> GetActiveChats()
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

            // Kullanıcının aktif sohbetlerini al
            var sql = @"
                SELECT 
                    mc.id as chat_id,
                    mc.match_id,
                    t.name as table_name,
                    m.status as match_status,
                    m.created_at as match_started_at
                FROM match_chats mc
                JOIN matches m ON mc.match_id = m.id
                JOIN tables t ON m.table_id = t.id
                JOIN match_players mp ON m.id = mp.match_id
                WHERE mp.user_id = @UserId
                  AND m.status IN ('active', 'finished')
                ORDER BY m.created_at DESC";

            var chats = await conn.QueryAsync<dynamic>(sql, new { UserId = userId });

            if (chats == null)
                return Ok(new { 
                    success = true, 
                    message = "Aktif sohbet bulunamadı",
                    chats = new List<object>() 
                });

            var activeChats = chats.Select(c => new
            {
                chatId = c?.chat_id ?? 0,
                matchId = c?.match_id ?? 0,
                tableName = c?.table_name?.ToString() ?? "Bilinmeyen Masa",
                matchStatus = c?.match_status?.ToString() ?? "unknown",
                matchStartedAt = c?.match_started_at
            });

            return Ok(new { 
                success = true, 
                message = $"{activeChats.Count()} aktif sohbet bulundu",
                chats = activeChats 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Aktif sohbetler alınırken bir hata oluştu", error = ex.Message });
        }
    }
} 