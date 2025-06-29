using Dapper;
using Npgsql;
using w101.Api.DTOs;

namespace w101.Api.Services
{
    public class ProfileService
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public ProfileService(IConfiguration configuration, string connectionString)
        {
            _configuration = configuration;
            _connectionString = connectionString;
        }

        private string GetConnectionString()
        {
            return _connectionString;
        }

        public async Task<ProfileResponse?> GetProfileAsync(int userId)
        {
            using var connection = new NpgsqlConnection(GetConnectionString());
            
            var profile = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT 
                    u.id,
                    u.username,
                    u.display_name,
                    u.email,
                    u.avatar_url,
                    u.level,
                    u.diamonds,
                    u.wins,
                    u.losses,
                    u.rank_id,
                    u.first_name,
                    u.last_name,
                    r.name as rank_name,
                    r.icon_url as rank_icon
                FROM users u
                LEFT JOIN ranks r ON u.rank_id = r.id
                WHERE u.id = @UserId",
                new { UserId = userId }
            );

            if (profile == null)
            {
                return null;
            }

            var totalMatches = profile.wins + profile.losses;
            var winRate = totalMatches > 0 ? (double)profile.wins / totalMatches * 100 : 0;

            return new ProfileResponse
            {
                Id = profile.id,
                Username = profile.username,
                DisplayName = profile.display_name,
                Email = profile.email,
                AvatarUrl = profile.avatar_url,
                Level = profile.level,
                Diamonds = profile.diamonds,
                Wins = profile.wins,
                Losses = profile.losses,
                TotalMatches = totalMatches,
                WinRate = Math.Round(winRate, 2),
                RankId = profile.rank_id,
                RankName = profile.rank_name,
                RankIcon = profile.rank_icon,
                FirstName = profile.first_name,
                LastName = profile.last_name
            };
        }

        public async Task<bool> UpdateProfileAsync(int userId, UpdateProfileRequest request)
        {
            using var connection = new NpgsqlConnection(GetConnectionString());
            
            var rowsAffected = await connection.ExecuteAsync(@"
                UPDATE users 
                SET 
                    display_name = COALESCE(@DisplayName, display_name),
                    avatar_url = COALESCE(@AvatarUrl, avatar_url),
                    first_name = COALESCE(@FirstName, first_name),
                    last_name = COALESCE(@LastName, last_name),
                    updated_at = @UpdatedAt
                WHERE id = @UserId",
                new 
                { 
                    UserId = userId,
                    request.DisplayName,
                    request.AvatarUrl,
                    request.FirstName,
                    request.LastName,
                    UpdatedAt = DateTime.UtcNow
                }
            );

            return rowsAffected > 0;
        }
    }
} 