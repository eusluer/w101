using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;
using System.ComponentModel.DataAnnotations;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/matches")]
public class MatchController : ControllerBase
{
    private readonly IConfiguration _config;

    public MatchController(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public class StartMatchRequest
    {
        [Required]
        public int TableId { get; set; }
    }

    public class FinishMatchRequest
    {
        [Required]
        public int MatchId { get; set; }
        
        [Required]
        public int WinnerUserId { get; set; }
        
        [Required]
        public List<PlayerResult> PlayerResults { get; set; } = new();
    }

    public class PlayerResult
    {
        [Required]
        public int UserId { get; set; }
        
        [Required]
        public int DiamondChange { get; set; } // Pozitif = kazanç, Negatif = kayıp
        
        [Required]
        public int Position { get; set; } // Maçtaki sıralaması (1 = kazanan)
    }

    [HttpPost("start")]
    [Authorize]
    public async Task<IActionResult> StartMatch([FromBody] StartMatchRequest req)
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
            
            // Masa bilgilerini al
            var table = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT id, name, min_bet, max_bet, status, lobby_id FROM tables WHERE id = @TableId", 
                new { TableId = req.TableId });
            
            if (table == null)
                return NotFound(new { message = "Masa bulunamadı" });

            // Masa durumu kontrolü - null check ekle
            var tableStatus = table.status?.ToString();
            if (string.IsNullOrEmpty(tableStatus) || tableStatus != "waiting")
                return BadRequest(new { message = "Bu masa zaten oyunda veya kapalı" });

            // Masadaki oyuncuları al
            var players = await conn.QueryAsync<dynamic>(@"
                SELECT 
                    tp.id as table_player_id,
                    tp.user_id,
                    tp.diamond_bet,
                    u.username,
                    u.diamonds
                FROM table_players tp
                INNER JOIN users u ON tp.user_id = u.id
                WHERE tp.table_id = @TableId", 
                new { TableId = req.TableId });

            var playerList = players?.ToList();
            if (playerList == null || !playerList.Any())
                return BadRequest(new { message = "Masada hiç oyuncu bulunamadı" });

            // 4 oyuncu kontrolü
            if (playerList.Count != 4)
                return BadRequest(new { 
                    message = "Maç başlatmak için 4 oyuncu gerekli",
                    currentPlayers = playerList.Count 
                });

            // Kullanıcının masada olup olmadığını kontrol et - null check ekle
            var userInTable = playerList.Any(p => p?.user_id == userId);
            if (!userInTable)
                return BadRequest(new { message = "Bu masada oturmuyorsunuz" });

            using var transaction = conn.BeginTransaction();
            try
            {
                // Maç oluştur
                var createMatchSql = @"
                    INSERT INTO matches (table_id, status, started_at) 
                    VALUES (@TableId, 'active', NOW()) 
                    RETURNING id";

                var matchId = await conn.ExecuteScalarAsync<int>(createMatchSql, new {
                    TableId = req.TableId
                }, transaction);

                // Oyuncuları match_players tablosuna ekle
                var addMatchPlayerSql = @"
                    INSERT INTO match_players (match_id, user_id, diamond_bet, position) 
                    VALUES (@MatchId, @UserId, @DiamondBet, @Position)";

                var position = 1;
                foreach (var player in playerList)
                {
                    await conn.ExecuteAsync(addMatchPlayerSql, new {
                        MatchId = matchId,
                        UserId = player.user_id,
                        DiamondBet = player.diamond_bet,
                        Position = position++
                    }, transaction);
                }

                // Masa durumunu güncelle
                await conn.ExecuteAsync(
                    "UPDATE tables SET status = 'in_game' WHERE id = @TableId", 
                    new { TableId = req.TableId }, transaction);

                transaction.Commit();

                return Ok(new { 
                    success = true, 
                    message = "Maç başarıyla başlatıldı",
                    match = new {
                        id = matchId,
                        tableId = req.TableId,
                        tableName = table.name,
                        status = "active",
                        playerCount = playerList.Count,
                        players = playerList.Select(p => new {
                            userId = p.user_id,
                            username = p.username,
                            diamondBet = p.diamond_bet
                        })
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
            return StatusCode(500, new { message = "Maç başlatılırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpPost("finish")]
    [Authorize]
    public async Task<IActionResult> FinishMatch([FromBody] FinishMatchRequest req)
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
            
            // Maç bilgilerini al
            var match = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    m.id,
                    m.table_id,
                    m.status,
                    m.started_at,
                    t.name as table_name,
                    t.lobby_id,
                    l.name as lobby_name
                FROM matches m
                INNER JOIN tables t ON m.table_id = t.id
                INNER JOIN lobbies l ON t.lobby_id = l.id
                WHERE m.id = @MatchId", 
                new { MatchId = req.MatchId });
            
            if (match == null)
                return NotFound(new { message = "Maç bulunamadı" });

            // Maç durumu kontrolü - null check ekle
            var matchStatus = match.status?.ToString();
            if (string.IsNullOrEmpty(matchStatus) || matchStatus != "active")
                return BadRequest(new { message = "Bu maç zaten bitmiş veya aktif değil" });

            // Maçtaki oyuncuları al
            var matchPlayers = await conn.QueryAsync<dynamic>(@"
                SELECT 
                    mp.id as match_player_id,
                    mp.user_id,
                    mp.diamond_bet,
                    u.username,
                    u.diamonds
                FROM match_players mp
                INNER JOIN users u ON mp.user_id = u.id
                WHERE mp.match_id = @MatchId", 
                new { MatchId = req.MatchId });

            var playerList = matchPlayers?.ToList();
            if (playerList == null || !playerList.Any())
                return BadRequest(new { message = "Maçta hiç oyuncu bulunamadı" });

            // Kullanıcının maçta olup olmadığını kontrol et - null check ekle
            var userInMatch = playerList.Any(p => p?.user_id == userId);
            if (!userInMatch)
                return BadRequest(new { message = "Bu maçta yer almıyorsunuz" });

            // Kazanan kontrolü - null check ekle
            var winner = playerList.FirstOrDefault(p => p?.user_id == req.WinnerUserId);
            if (winner == null)
                return BadRequest(new { message = "Kazanan oyuncu bu maçta yer almıyor" });

            // PlayerResults kontrolü - null check ekle
            if (req.PlayerResults == null || !req.PlayerResults.Any())
                return BadRequest(new { message = "Oyuncu sonuçları belirtilmelidir" });

            // PlayerResults kontrolü
            if (req.PlayerResults.Count != playerList.Count)
                return BadRequest(new { message = "Tüm oyuncular için sonuç girilmelidir" });

            using var transaction = conn.BeginTransaction();
            try
            {
                // Maçı bitir
                await conn.ExecuteAsync(@"
                    UPDATE matches 
                    SET status = 'finished'
                    WHERE id = @MatchId", 
                    new { MatchId = req.MatchId }, transaction);

                // Her oyuncu için işlemler
                foreach (var result in req.PlayerResults)
                {
                    var player = playerList.FirstOrDefault(p => p.user_id == result.UserId);
                    if (player == null) continue;

                    // Oyuncunun elmasını güncelle
                    await conn.ExecuteAsync(@"
                        UPDATE users 
                        SET diamonds = diamonds + @DiamondChange 
                        WHERE id = @UserId", 
                        new { UserId = result.UserId, DiamondChange = result.DiamondChange }, transaction);

                    // Diamond transaction kaydı
                    await conn.ExecuteAsync(@"
                        INSERT INTO diamond_transactions (user_id, amount, transaction_type, description, created_at) 
                        VALUES (@UserId, @Amount, @Type, @Description, NOW())", 
                        new { 
                            UserId = result.UserId,
                            Amount = Math.Abs(result.DiamondChange),
                            Type = result.DiamondChange > 0 ? "match_win" : "match_loss",
                            Description = result.DiamondChange > 0 ? 
                                $"Maç kazancı - {match.table_name}" : 
                                $"Maç kaybı - {match.table_name}"
                        }, transaction);

                    // Match history tablosu mevcut değil, match_players tablosunda saklıyoruz

                    // Match players güncelle
                    await conn.ExecuteAsync(@"
                        UPDATE match_players 
                        SET final_position = @Position, diamond_change = @DiamondChange 
                        WHERE match_id = @MatchId AND user_id = @UserId", 
                        new { 
                            MatchId = req.MatchId,
                            UserId = result.UserId,
                            Position = result.Position,
                            DiamondChange = result.DiamondChange
                        }, transaction);
                }

                // Masayı temizle
                await conn.ExecuteAsync(
                    "DELETE FROM table_players WHERE table_id = @TableId", 
                    new { TableId = match.table_id }, transaction);

                // Masa durumunu güncelle
                await conn.ExecuteAsync(
                    "UPDATE tables SET status = 'waiting' WHERE id = @TableId", 
                    new { TableId = match.table_id }, transaction);

                transaction.Commit();

                return Ok(new { 
                    success = true, 
                    message = "Maç başarıyla bitirildi",
                    match = new {
                        id = req.MatchId,
                        tableId = match.table_id,
                        tableName = match.table_name,
                        lobbyName = match.lobby_name,
                        status = "finished",
                        winner = new {
                            userId = req.WinnerUserId,
                            username = winner.username
                        },
                        results = req.PlayerResults.Select(r => new {
                            userId = r.UserId,
                            username = playerList.FirstOrDefault(p => p?.user_id == r.UserId)?.username ?? "Bilinmeyen",
                            diamondChange = r.DiamondChange,
                            position = r.Position,
                            isWinner = r.UserId == req.WinnerUserId
                        })
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
            return StatusCode(500, new { message = "Maç bitirilirken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetMatchHistory()
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
            
            // Basit match listesi (şemaya uygun)
            var sql = @"
                SELECT 
                    m.id as match_id,
                    m.status,
                    m.started_at,
                    t.name as table_name,
                    l.name as lobby_name
                FROM matches m
                INNER JOIN tables t ON m.table_id = t.id
                INNER JOIN lobbies l ON t.lobby_id = l.id
                INNER JOIN match_players mp ON m.id = mp.match_id
                WHERE mp.user_id = @UserId
                ORDER BY m.started_at DESC
                LIMIT 20";

            var history = await conn.QueryAsync<dynamic>(sql, new { UserId = userId });
            
            if (history == null)
                return Ok(new { 
                    success = true, 
                    message = "Hiç maç geçmişi bulunamadı",
                    matches = new List<object>() 
                });

            var matchHistory = history.Select(h => new
            {
                matchId = h?.match_id ?? 0,
                tableName = h?.table_name?.ToString() ?? "Bilinmeyen Masa",
                lobbyName = h?.lobby_name?.ToString() ?? "Bilinmeyen Lobi",
                status = h?.status?.ToString() ?? "Bilinmeyen",
                startedAt = h?.started_at,
                isWinner = false, // Şimdilik false, gerçek değer için winner_user_id gerekli
                diamondChange = 0, // Şimdilik 0, gerçek değer için ayrı sorgu gerekli
                winnerUsername = "TBD" // To be determined
            });

            return Ok(new { 
                success = true, 
                message = $"Son {matchHistory.Count()} maç geçmişi getirildi",
                matches = matchHistory 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Maç geçmişi alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("recent")]
    [Authorize]
    public async Task<IActionResult> GetRecentMatches()
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
                    COALESCE(m.match_type, 'Lobi Oyunu') as match_type,
                    COALESCE(l.difficulty, 'Orta') as difficulty,
                    COALESCE(mp.score, 0) as score,
                    CASE WHEN mp.final_position = 1 THEN 'win' ELSE 'lose' END as result,
                    m.started_at as created_at,
                    CASE 
                        WHEN m.started_at > NOW() - INTERVAL '1 hour' THEN ROUND(EXTRACT(EPOCH FROM (NOW() - m.started_at))/60) || ' dakika önce'
                        WHEN m.started_at > NOW() - INTERVAL '1 day' THEN ROUND(EXTRACT(EPOCH FROM (NOW() - m.started_at))/3600) || ' saat önce'
                        ELSE ROUND(EXTRACT(EPOCH FROM (NOW() - m.started_at))/86400) || ' gün önce'
                    END as time_ago
                FROM matches m
                JOIN match_players mp ON m.id = mp.match_id
                LEFT JOIN tables t ON m.table_id = t.id
                LEFT JOIN lobbies l ON t.lobby_id = l.id
                WHERE mp.user_id = @UserId
                ORDER BY m.started_at DESC
                LIMIT 10";

            var recentMatches = await conn.QueryAsync<dynamic>(sql, new { UserId = userId });

            if (recentMatches == null)
                return Ok(new { 
                    success = true, 
                    message = "Hiç son oyun bulunamadı",
                    matches = new List<object>() 
                });

            var matches = recentMatches.Select(m => new
            {
                matchType = m?.match_type?.ToString() ?? "Lobi Oyunu",
                difficulty = m?.difficulty?.ToString() ?? "Orta",
                score = m?.score ?? 0,
                result = m?.result?.ToString() ?? "lose",
                createdAt = m?.created_at,
                timeAgo = m?.time_ago?.ToString() ?? "Bilinmeyen"
            });

            return Ok(new { 
                success = true, 
                message = $"Son {matches.Count()} oyun getirildi",
                matches = matches 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Son oyunlar alınırken bir hata oluştu", error = ex.Message });
        }
    }
} 