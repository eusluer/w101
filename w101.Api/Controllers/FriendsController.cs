using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;
using System.ComponentModel.DataAnnotations;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/friends")]
public class FriendsController : ControllerBase
{
    private readonly IConfiguration _config;

    public FriendsController(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public class FriendRequestModel
    {
        [Required]
        public int TargetUserId { get; set; }
    }

    public class FriendRequestResponseModel
    {
        [Required]
        public int RequestId { get; set; }
        
        [Required]
        public bool Accept { get; set; }
    }

    [HttpPost("request")]
    [Authorize]
    public async Task<IActionResult> SendFriendRequest([FromBody] FriendRequestModel req)
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

            // Kendi kendine istek gönderemez
            if (userId == req.TargetUserId)
                return BadRequest(new { message = "Kendinize arkadaşlık isteği gönderemezsiniz" });

            // Hedef kullanıcı var mı kontrol et
            var targetUser = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id, username FROM users WHERE id = @UserId", 
                new { UserId = req.TargetUserId });
            
            if (targetUser == null)
                return NotFound(new { message = "Hedef kullanıcı bulunamadı" });

            // Zaten arkadaş mı kontrol et
            var existingFriendship = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT id FROM friendships 
                WHERE (user_id = @UserId AND friend_id = @TargetUserId) 
                   OR (user_id = @TargetUserId AND friend_id = @UserId)", 
                new { UserId = userId, TargetUserId = req.TargetUserId });

            if (existingFriendship != null)
                return BadRequest(new { message = "Bu kullanıcı zaten arkadaşınız" });

            // Bekleyen istek var mı kontrol et
            var existingRequest = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT id FROM friend_requests 
                WHERE ((from_user_id = @UserId AND to_user_id = @TargetUserId) 
                    OR (from_user_id = @TargetUserId AND to_user_id = @UserId))
                  AND status = 'pending'", 
                new { UserId = userId, TargetUserId = req.TargetUserId });

            if (existingRequest != null)
                return BadRequest(new { message = "Bu kullanıcıyla zaten bekleyen bir arkadaşlık isteği var" });

            // Yeni arkadaşlık isteği oluştur
            var requestId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO friend_requests (from_user_id, to_user_id, status, created_at) 
                VALUES (@FromUserId, @ToUserId, 'pending', NOW()) 
                RETURNING id", 
                new { FromUserId = userId, ToUserId = req.TargetUserId });

            return Ok(new { 
                success = true, 
                message = "Arkadaşlık isteği başarıyla gönderildi",
                requestId = requestId
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Arkadaşlık isteği gönderilirken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("requests")]
    [Authorize]
    public async Task<IActionResult> GetFriendRequests()
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
                    fr.id as request_id,
                    u_from.username as from_user,
                    u_to.username as to_user,
                    fr.status,
                    fr.created_at
                FROM friend_requests fr
                JOIN users u_from ON fr.from_user_id = u_from.id
                JOIN users u_to ON fr.to_user_id = u_to.id
                WHERE fr.from_user_id = @UserId OR fr.to_user_id = @UserId
                ORDER BY fr.created_at DESC";

            var requests = await conn.QueryAsync<dynamic>(sql, new { UserId = userId });

            if (requests == null)
                return Ok(new { 
                    success = true, 
                    message = "Hiç arkadaşlık isteği bulunamadı",
                    requests = new List<object>() 
                });

            var friendRequests = requests.Select(r => new
            {
                requestId = r?.request_id ?? 0,
                fromUser = r?.from_user?.ToString() ?? "Bilinmeyen",
                toUser = r?.to_user?.ToString() ?? "Bilinmeyen",
                status = r?.status?.ToString() ?? "unknown",
                createdAt = r?.created_at
            });

            return Ok(new { 
                success = true, 
                message = $"{friendRequests.Count()} arkadaşlık isteği bulundu",
                requests = friendRequests 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Arkadaşlık istekleri alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpPost("respond")]
    [Authorize]
    public async Task<IActionResult> RespondToFriendRequest([FromBody] FriendRequestResponseModel req)
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

            // İsteği kontrol et (sadece kendine gelen istekleri yanıtlayabilir)
            var request = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id, from_user_id, to_user_id, status FROM friend_requests WHERE id = @RequestId AND to_user_id = @UserId", 
                new { RequestId = req.RequestId, UserId = userId });
            
            if (request == null)
                return NotFound(new { message = "Arkadaşlık isteği bulunamadı veya yanıtlama yetkiniz yok" });

            if (request.status != "pending")
                return BadRequest(new { message = "Bu istek zaten yanıtlanmış" });

            using var transaction = conn.BeginTransaction();
            try
            {
                var newStatus = req.Accept ? "accepted" : "rejected";
                
                // İsteği güncelle
                await conn.ExecuteAsync(@"
                    UPDATE friend_requests 
                    SET status = @Status, responded_at = NOW() 
                    WHERE id = @RequestId", 
                    new { Status = newStatus, RequestId = req.RequestId }, transaction);

                // Kabul edildiyse arkadaşlık oluştur
                if (req.Accept)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO friendships (user_id, friend_id, created_at) 
                        VALUES (@UserId, @FriendId, NOW())", 
                        new { UserId = userId, FriendId = request.from_user_id }, transaction);

                    await conn.ExecuteAsync(@"
                        INSERT INTO friendships (user_id, friend_id, created_at) 
                        VALUES (@UserId, @FriendId, NOW())", 
                        new { UserId = request.from_user_id, FriendId = userId }, transaction);
                }

                transaction.Commit();

                var message = req.Accept ? "Arkadaşlık isteği kabul edildi" : "Arkadaşlık isteği reddedildi";
                return Ok(new { success = true, message = message });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Arkadaşlık isteği yanıtlanırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("search")]
    [Authorize]
    public async Task<IActionResult> SearchFriends([FromQuery] string query)
    {
        try
        {
            if (string.IsNullOrEmpty(query) || query.Length < 2)
                return BadRequest(new { message = "Arama sorgusu en az 2 karakter olmalıdır" });

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
                    u.id as user_id,
                    u.username,
                    u.display_name,
                    u.avatar_url,
                    COALESCE(r.name, 'Başlangıç') as rank_name
                FROM users u
                LEFT JOIN ranks r ON u.rank_id = r.id
                WHERE u.id != @UserId
                  AND (u.username ILIKE @Query OR u.display_name ILIKE @Query)
                ORDER BY 
                    CASE WHEN u.username ILIKE @ExactQuery THEN 1 ELSE 2 END,
                    u.username
                LIMIT 20";

            var users = await conn.QueryAsync<dynamic>(sql, new { 
                UserId = userId,
                Query = $"%{query}%",
                ExactQuery = query
            });

            if (users == null)
                return Ok(new { 
                    success = true, 
                    message = $"'{query}' araması için kullanıcı bulunamadı",
                    users = new List<object>() 
                });

            var searchResults = users.Select(u => new
            {
                userId = u?.user_id ?? 0,
                username = u?.username?.ToString() ?? "Bilinmeyen",
                displayName = u?.display_name?.ToString() ?? "Bilinmeyen",
                avatarUrl = u?.avatar_url?.ToString() ?? "",
                rankName = u?.rank_name?.ToString() ?? "Başlangıç"
            });

            return Ok(new { 
                success = true, 
                message = $"'{query}' araması için {searchResults.Count()} kullanıcı bulundu",
                query = query,
                users = searchResults 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Arkadaş araması yapılırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("list")]
    [Authorize]
    public async Task<IActionResult> GetFriendsList()
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
                    u.id as user_id,
                    u.display_name,
                    u.avatar_url,
                    CASE WHEN u.last_active > NOW() - INTERVAL '5 minutes' THEN 'online' ELSE 'offline' END as status
                FROM friendships f
                JOIN users u ON f.friend_id = u.id
                WHERE f.user_id = @UserId
                ORDER BY u.display_name";

            var friends = await conn.QueryAsync<dynamic>(sql, new { UserId = userId });

            if (friends == null)
                return Ok(new { 
                    success = true, 
                    message = "Hiç arkadaş bulunamadı",
                    friends = new List<object>() 
                });

            var friendsList = friends.Select(f => new
            {
                userId = f?.user_id ?? 0,
                displayName = f?.display_name?.ToString() ?? "Bilinmeyen",
                avatarUrl = f?.avatar_url?.ToString() ?? "",
                status = f?.status?.ToString() ?? "offline"
            });

            return Ok(new { 
                success = true, 
                message = $"{friendsList.Count()} arkadaş bulundu",
                friends = friendsList 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Arkadaş listesi alınırken bir hata oluştu", error = ex.Message });
        }
    }
} 