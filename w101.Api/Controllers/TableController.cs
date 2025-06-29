using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;
using System.ComponentModel.DataAnnotations;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/Table")]
public class TableController : ControllerBase
{
    private readonly IConfiguration _config;

    public TableController(IConfiguration config)
    {
        _config = config;
    }

    public class CreateTableRequest
    {
        [Required]
        public int LobbyId { get; set; }
        
        [Required]
        [StringLength(100, ErrorMessage = "Masa adı 100 karakterden fazla olamaz")]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Minimum bahis 1'den büyük olmalıdır")]
        public int MinBet { get; set; }
        
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Maksimum bahis 1'den büyük olmalıdır")]
        public int MaxBet { get; set; }
    }

    public class JoinTableRequest
    {
        [Required]
        public int TableId { get; set; }
        
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Bahis miktarı 1'den büyük olmalıdır")]
        public int DiamondBet { get; set; }
    }

    [HttpGet("lobby/{lobbyId}")]
    public async Task<IActionResult> GetTablesInLobby(int lobbyId)
    {
        try
        {
            using var conn = new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));
            
            // Lobi varlığını kontrol et
            var lobbyExists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM lobbies WHERE id = @LobbyId", 
                new { LobbyId = lobbyId });
            
            if (lobbyExists == 0)
                return NotFound(new { message = "Lobi bulunamadı" });

            // Masaları ve oyuncuları getir
            var tablesSql = @"
                SELECT 
                    t.id,
                    t.name,
                    t.min_bet,
                    t.max_bet,
                    t.status,
                    t.created_at
                FROM tables t
                WHERE t.lobby_id = @LobbyId
                ORDER BY t.created_at DESC";

            var tables = await conn.QueryAsync<dynamic>(tablesSql, new { LobbyId = lobbyId });

            var tablesWithPlayers = new List<object>();

            foreach (var table in tables)
            {
                // Her masa için oyuncuları getir
                var playersSql = @"
                    SELECT 
                        tp.id as player_table_id,
                        tp.diamond_bet,
                        tp.joined_at,
                        u.id as user_id,
                        u.username,
                        u.display_name,
                        u.avatar_url,
                        u.level,
                        u.diamonds
                    FROM table_players tp
                    INNER JOIN users u ON tp.user_id = u.id
                    WHERE tp.table_id = @TableId
                    ORDER BY tp.joined_at ASC";

                var players = await conn.QueryAsync<dynamic>(playersSql, new { TableId = table.id });

                var playerList = players.Select(p => new
                {
                    playerTableId = p.player_table_id,
                    diamondBet = p.diamond_bet,
                    joinedAt = p.joined_at,
                    user = new
                    {
                        id = p.user_id,
                        username = p.username,
                        displayName = p.display_name,
                        avatarUrl = p.avatar_url,
                        level = p.level,
                        diamonds = p.diamonds
                    }
                });

                tablesWithPlayers.Add(new
                {
                    id = table.id,
                    name = table.name,
                    minBet = table.min_bet,
                    maxBet = table.max_bet,
                    status = table.status,
                    createdAt = table.created_at,
                    playerCount = playerList.Count(),
                    players = playerList
                });
            }

            return Ok(new { 
                success = true, 
                lobbyId = lobbyId,
                tables = tablesWithPlayers 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Masalar alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpPost("create")]
    [Authorize]
    public async Task<IActionResult> CreateTable([FromBody] CreateTableRequest req)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (req.MinBet >= req.MaxBet)
                return BadRequest(new { message = "Minimum bahis, maksimum bahisten küçük olmalıdır" });

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
                return Unauthorized(new { message = "Geçersiz token" });

            var userId = int.Parse(userIdClaim.Value);

            using var conn = new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));
            
            // Kullanıcı bilgilerini al
            var user = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id, username, diamonds FROM users WHERE id = @UserId", 
                new { UserId = userId });
            
            if (user == null)
                return NotFound(new { message = "Kullanıcı bulunamadı" });

            // Lobi varlığını kontrol et
            var lobbyExists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM lobbies WHERE id = @LobbyId", 
                new { LobbyId = req.LobbyId });
            
            if (lobbyExists == 0)
                return NotFound(new { message = "Lobi bulunamadı" });

            // Kullanıcının minimum bahis için yeterli elması var mı kontrol et
            if (user.diamonds < req.MinBet)
            {
                return BadRequest(new { 
                    message = "Masa oluşturmak için yetersiz elmas",
                    required = req.MinBet,
                    current = user.diamonds 
                });
            }

            using var transaction = conn.BeginTransaction();
            try
            {
                // Masa oluştur
                var createTableSql = @"
                    INSERT INTO tables (lobby_id, name, min_bet, max_bet, status, created_at) 
                    VALUES (@LobbyId, @Name, @MinBet, @MaxBet, 'waiting', NOW()) 
                    RETURNING id";

                var tableId = await conn.ExecuteScalarAsync<int>(createTableSql, new {
                    LobbyId = req.LobbyId,
                    Name = req.Name,
                    MinBet = req.MinBet,
                    MaxBet = req.MaxBet
                }, transaction);

                // Owner'ı ilk oyuncu olarak ekle
                var addPlayerSql = @"
                    INSERT INTO table_players (table_id, user_id, diamond_bet, joined_at) 
                    VALUES (@TableId, @UserId, @DiamondBet, NOW())";

                await conn.ExecuteAsync(addPlayerSql, new {
                    TableId = tableId,
                    UserId = userId,
                    DiamondBet = req.MinBet
                }, transaction);

                transaction.Commit();

                return Ok(new { 
                    success = true, 
                    message = "Masa başarıyla oluşturuldu",
                    table = new {
                        id = tableId,
                        name = req.Name,
                        minBet = req.MinBet,
                        maxBet = req.MaxBet,
                        lobbyId = req.LobbyId,
                        owner = new {
                            id = user.id,
                            username = user.username
                        }
                    }
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
            return StatusCode(500, new { message = "Masa oluşturulurken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpPost("join")]
    [Authorize]
    public async Task<IActionResult> JoinTable([FromBody] JoinTableRequest req)
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
            
            // Kullanıcı bilgilerini al
            var user = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id, username, diamonds FROM users WHERE id = @UserId", 
                new { UserId = userId });
            
            if (user == null)
                return NotFound(new { message = "Kullanıcı bulunamadı" });

            // Masa bilgilerini al
            var table = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id, name, min_bet, max_bet, status FROM tables WHERE id = @TableId", 
                new { TableId = req.TableId });
            
            if (table == null)
                return NotFound(new { message = "Masa bulunamadı" });

            // Masa durumu kontrolü
            if (table.status != "waiting")
                return BadRequest(new { message = "Bu masaya şu anda katılamazsınız" });

            // Bahis miktarı kontrolü
            if (req.DiamondBet < table.min_bet || req.DiamondBet > table.max_bet)
            {
                return BadRequest(new { 
                    message = "Geçersiz bahis miktarı",
                    minBet = table.min_bet,
                    maxBet = table.max_bet,
                    yourBet = req.DiamondBet
                });
            }

            // Kullanıcının yeterli elması var mı kontrol et
            if (user.diamonds < req.DiamondBet)
            {
                return BadRequest(new { 
                    message = "Yetersiz elmas",
                    required = req.DiamondBet,
                    current = user.diamonds 
                });
            }

            // Kullanıcı zaten bu masada mı kontrol et
            var alreadyJoined = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM table_players WHERE table_id = @TableId AND user_id = @UserId",
                new { TableId = req.TableId, UserId = userId });

            if (alreadyJoined > 0)
                return BadRequest(new { message = "Bu masada zaten oturuyorsunuz" });

            // Mevcut oyuncu sayısını al
            var currentPlayerCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM table_players WHERE table_id = @TableId",
                new { TableId = req.TableId });

            // Maksimum 4 oyuncu varsayımı (isteğe göre değiştirilebilir)
            if (currentPlayerCount >= 4)
                return BadRequest(new { message = "Masa dolu" });

            // Oyuncuyu masaya ekle
            var addPlayerSql = @"
                INSERT INTO table_players (table_id, user_id, diamond_bet, joined_at) 
                VALUES (@TableId, @UserId, @DiamondBet, NOW())";

            await conn.ExecuteAsync(addPlayerSql, new {
                TableId = req.TableId,
                UserId = userId,
                DiamondBet = req.DiamondBet
            });

            return Ok(new { 
                success = true, 
                message = "Masaya başarıyla oturdunuz",
                table = new {
                    id = table.id,
                    name = table.name,
                    minBet = table.min_bet,
                    maxBet = table.max_bet
                },
                player = new {
                    id = user.id,
                    username = user.username,
                    diamondBet = req.DiamondBet
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Masaya oturma sırasında bir hata oluştu", error = ex.Message });
        }
    }
} 