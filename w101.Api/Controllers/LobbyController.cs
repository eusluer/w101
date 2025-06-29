using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Npgsql;
using Dapper;
using System.ComponentModel.DataAnnotations;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/Lobby")]
public class LobbyController : ControllerBase
{
    private readonly IConfiguration _config;

    public LobbyController(IConfiguration config)
    {
        _config = config;
    }

    public class JoinLobbyRequest
    {
        [Required]
        public int LobbyId { get; set; }
    }

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new { message = "Lobby controller çalışıyor!", timestamp = DateTime.Now });
    }

    [HttpGet]
    public async Task<IActionResult> GetLobbies()
    {
        try
        {
            using var conn = new NpgsqlConnection(_config.GetConnectionString("DefaultConnection"));
            
            var sql = @"
                SELECT 
                    l.id,
                    l.name,
                    l.min_diamonds,
                    l.max_diamonds,
                    l.min_rank_id,
                    l.max_rank_id,
                    r1.name as min_rank_name,
                    r1.min_level as min_rank_min_level,
                    r2.name as max_rank_name,
                    r2.min_level as max_rank_min_level
                FROM lobbies l
                LEFT JOIN ranks r1 ON l.min_rank_id = r1.id
                LEFT JOIN ranks r2 ON l.max_rank_id = r2.id
                ORDER BY l.min_diamonds ASC";

            var lobbies = await conn.QueryAsync<dynamic>(sql);

            var lobbyList = lobbies.Select(lobby => new
            {
                id = lobby.id,
                name = lobby.name,
                minDiamonds = lobby.min_diamonds,
                maxDiamonds = lobby.max_diamonds,
                minRank = lobby.min_rank_id != null ? new
                {
                    id = lobby.min_rank_id,
                    name = lobby.min_rank_name,
                    minLevel = lobby.min_rank_min_level
                } : null,
                maxRank = lobby.max_rank_id != null ? new
                {
                    id = lobby.max_rank_id,
                    name = lobby.max_rank_name,
                    minLevel = lobby.max_rank_min_level
                } : null
            }).ToList();

            return Ok(new { 
                success = true, 
                message = $"Toplam {lobbyList.Count} lobi bulundu",
                lobbies = lobbyList 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                success = false,
                message = "Lobiler alınırken bir hata oluştu", 
                error = ex.Message 
            });
        }
    }

    [HttpPost("join")]
    [Authorize]
    public async Task<IActionResult> JoinLobby([FromBody] JoinLobbyRequest req)
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
            var userSql = "SELECT id, username, diamonds, level FROM users WHERE id = @UserId";
            var user = await conn.QueryFirstOrDefaultAsync<dynamic>(userSql, new { UserId = userId });
            
            if (user == null)
                return NotFound(new { message = "Kullanıcı bulunamadı" });

            // Lobi bilgilerini al
            var lobbySql = @"
                SELECT 
                    l.id,
                    l.name,
                    l.min_diamonds,
                    l.max_diamonds,
                    l.min_rank_id,
                    l.max_rank_id,
                    r1.min_level as min_rank_min_level,
                    r2.min_level as max_rank_min_level
                FROM lobbies l
                LEFT JOIN ranks r1 ON l.min_rank_id = r1.id
                LEFT JOIN ranks r2 ON l.max_rank_id = r2.id
                WHERE l.id = @LobbyId";

            var lobby = await conn.QueryFirstOrDefaultAsync<dynamic>(lobbySql, new { LobbyId = req.LobbyId });
            
            if (lobby == null)
                return NotFound(new { message = "Lobi bulunamadı" });

            // Elmas kontrolü
            if (user.diamonds < lobby.min_diamonds)
            {
                return BadRequest(new { 
                    message = "Yetersiz elmas", 
                    required = lobby.min_diamonds,
                    current = user.diamonds 
                });
            }

            if (lobby.max_diamonds.HasValue && user.diamonds > lobby.max_diamonds)
            {
                return BadRequest(new { 
                    message = "Çok fazla elmas", 
                    maxAllowed = lobby.max_diamonds,
                    current = user.diamonds 
                });
            }

            // Seviye kontrolü (eğer lobi rank gerektiriyorsa)
            if (lobby.min_rank_min_level.HasValue && user.level < lobby.min_rank_min_level)
            {
                return BadRequest(new { 
                    message = "Yetersiz seviye", 
                    required = lobby.min_rank_min_level,
                    current = user.level 
                });
            }

            if (lobby.max_rank_min_level.HasValue && user.level < lobby.max_rank_min_level)
            {
                return BadRequest(new { 
                    message = "Yetersiz seviye", 
                    required = lobby.max_rank_min_level,
                    current = user.level 
                });
            }

            return Ok(new { 
                success = true, 
                message = "Lobiye giriş onaylandı",
                lobby = new {
                    id = lobby.id,
                    name = lobby.name,
                    minDiamonds = lobby.min_diamonds,
                    maxDiamonds = lobby.max_diamonds
                },
                user = new {
                    id = user.id,
                    username = user.username,
                    diamonds = user.diamonds,
                    level = user.level
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lobiye giriş kontrolü sırasında bir hata oluştu", error = ex.Message });
        }
    }
} 