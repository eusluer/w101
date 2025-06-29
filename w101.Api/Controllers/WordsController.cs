using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Dapper;

namespace w101.Api.Controllers;

[ApiController]
[Route("api/words")]
public class WordsController : ControllerBase
{
    private readonly IConfiguration _config;

    public WordsController(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    [HttpGet]
    public async Task<IActionResult> GetWords([FromQuery] string lang = "tr")
    {
        try
        {
            if (string.IsNullOrEmpty(lang))
                lang = "tr"; // Default Türkçe

            var connectionString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
                return StatusCode(500, new { message = "Veritabanı bağlantı yapılandırması bulunamadı" });

            using var conn = new NpgsqlConnection(connectionString);
            
            var sql = @"
                SELECT 
                    word,
                    language
                FROM words 
                WHERE language = @Language
                  AND is_active = true
                ORDER BY word";

            var words = await conn.QueryAsync<dynamic>(sql, new { Language = lang });

            if (words == null)
                return Ok(new { 
                    success = true, 
                    message = $"'{lang}' dili için kelime bulunamadı",
                    words = new List<object>() 
                });

            var wordList = words.Select(w => new
            {
                word = w?.word?.ToString() ?? "",
                language = w?.language?.ToString() ?? lang
            });

            return Ok(new { 
                success = true, 
                message = $"{wordList.Count()} adet '{lang}' kelimesi bulundu",
                language = lang,
                words = wordList 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Kelime listesi alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("random")]
    public async Task<IActionResult> GetRandomWords([FromQuery] string lang = "tr", [FromQuery] int count = 10)
    {
        try
        {
            if (string.IsNullOrEmpty(lang))
                lang = "tr"; // Default Türkçe

            if (count <= 0 || count > 100)
                count = 10; // Default 10 kelime, maksimum 100

            var connectionString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
                return StatusCode(500, new { message = "Veritabanı bağlantı yapılandırması bulunamadı" });

            using var conn = new NpgsqlConnection(connectionString);
            
            var sql = @"
                SELECT 
                    word,
                    language
                FROM words 
                WHERE language = @Language
                  AND is_active = true
                ORDER BY RANDOM()
                LIMIT @Count";

            var words = await conn.QueryAsync<dynamic>(sql, new { Language = lang, Count = count });

            if (words == null)
                return Ok(new { 
                    success = true, 
                    message = $"'{lang}' dili için kelime bulunamadı",
                    words = new List<object>() 
                });

            var wordList = words.Select(w => new
            {
                word = w?.word?.ToString() ?? "",
                language = w?.language?.ToString() ?? lang
            });

            return Ok(new { 
                success = true, 
                message = $"{wordList.Count()} adet rastgele '{lang}' kelimesi bulundu",
                language = lang,
                count = wordList.Count(),
                words = wordList 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Rastgele kelime listesi alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("languages")]
    public async Task<IActionResult> GetAvailableLanguages()
    {
        try
        {
            var connectionString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
                return StatusCode(500, new { message = "Veritabanı bağlantı yapılandırması bulunamadı" });

            using var conn = new NpgsqlConnection(connectionString);
            
            var sql = @"
                SELECT 
                    language,
                    COUNT(*) as word_count
                FROM words 
                WHERE is_active = true
                GROUP BY language
                ORDER BY language";

            var languages = await conn.QueryAsync<dynamic>(sql);

            if (languages == null)
                return Ok(new { 
                    success = true, 
                    message = "Dil bulunamadı",
                    languages = new List<object>() 
                });

            var languageList = languages.Select(l => new
            {
                language = l?.language?.ToString() ?? "unknown",
                wordCount = l?.word_count ?? 0
            });

            return Ok(new { 
                success = true, 
                message = $"{languageList.Count()} dil mevcut",
                languages = languageList 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Dil listesi alınırken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("download")]
    public async Task<IActionResult> DownloadWords([FromQuery] string lang = "tr", [FromQuery] string version = "")
    {
        try
        {
            if (string.IsNullOrEmpty(lang))
                lang = "tr"; // Default Türkçe

            var connectionString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
                return StatusCode(500, new { message = "Veritabanı bağlantı yapılandırması bulunamadı" });

            using var conn = new NpgsqlConnection(connectionString);
            
            // Mevcut versiyon numarasını al
            var currentVersion = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    version,
                    updated_at
                FROM word_versions 
                WHERE language = @Language
                ORDER BY updated_at DESC
                LIMIT 1", 
                new { Language = lang });

            var latestVersion = currentVersion?.version?.ToString() ?? "1.0";

            // Versiyon kontrolü - eğer client'ın versiyonu güncel ise, boş response döndür
            if (!string.IsNullOrEmpty(version) && version == latestVersion)
            {
                return Ok(new { 
                    success = true,
                    message = "Kelime listesi güncel",
                    version = latestVersion,
                    isUpToDate = true,
                    wordCount = 0,
                    words = ""
                });
            }

            // Kelime listesini al
            var sql = @"
                SELECT word
                FROM words 
                WHERE language = @Language
                  AND is_active = true
                ORDER BY word";

            var words = await conn.QueryAsync<dynamic>(sql, new { Language = lang });

            if (words == null || !words.Any())
            {
                return Ok(new { 
                    success = true, 
                    message = $"'{lang}' dili için kelime bulunamadı",
                    version = latestVersion,
                    isUpToDate = false,
                    wordCount = 0,
                    words = ""
                });
            }

            // Kelimeleri text formatında birleştir (her satırda bir kelime)
            var wordList = words.Select(w => w?.word?.ToString() ?? "").Where(w => !string.IsNullOrEmpty(w));
            var wordsText = string.Join("\n", wordList);

            // Text/plain response döndür
            Response.ContentType = "text/plain; charset=utf-8";
            Response.Headers["X-Word-Version"] = latestVersion;
            Response.Headers["X-Word-Count"] = wordList.Count().ToString();
            Response.Headers["X-Language"] = lang;

            return Content(wordsText, "text/plain");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Kelime listesi indirilirken bir hata oluştu", error = ex.Message });
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchWords([FromQuery] string query, [FromQuery] string lang = "tr")
    {
        try
        {
            if (string.IsNullOrEmpty(query) || query.Length < 2)
                return BadRequest(new { message = "Arama sorgusu en az 2 karakter olmalıdır" });

            if (string.IsNullOrEmpty(lang))
                lang = "tr"; // Default Türkçe

            var connectionString = _config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
                return StatusCode(500, new { message = "Veritabanı bağlantı yapılandırması bulunamadı" });

            using var conn = new NpgsqlConnection(connectionString);
            
            var sql = @"
                SELECT 
                    word,
                    language
                FROM words 
                WHERE language = @Language
                  AND is_active = true
                  AND word ILIKE @Query
                ORDER BY LENGTH(word), word
                LIMIT 50";

            var words = await conn.QueryAsync<dynamic>(sql, new { 
                Language = lang, 
                Query = $"%{query}%" 
            });

            if (words == null)
                return Ok(new { 
                    success = true, 
                    message = $"'{query}' araması için kelime bulunamadı",
                    words = new List<object>() 
                });

            var wordList = words.Select(w => new
            {
                word = w?.word?.ToString() ?? "",
                language = w?.language?.ToString() ?? lang
            });

            return Ok(new { 
                success = true, 
                message = $"'{query}' araması için {wordList.Count()} kelime bulundu",
                query = query,
                language = lang,
                words = wordList 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Kelime araması yapılırken bir hata oluştu", error = ex.Message });
        }
    }
} 