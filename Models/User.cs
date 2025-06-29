using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace w101.Api.Models;

[Table("users")]
public class User
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("username")]
    [MaxLength(255)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [Column("password_hash")]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("email")]
    [MaxLength(255)]
    public string? Email { get; set; }

    [Column("display_name")]
    [MaxLength(255)]
    public string? DisplayName { get; set; }

    [Column("avatar_url")]
    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    [Column("rank_id")]
    public int? RankId { get; set; }

    [Column("level")]
    public int Level { get; set; } = 1;

    [Column("diamonds")]
    public int Diamonds { get; set; } = 0;

    [Column("wins")]
    public int Wins { get; set; } = 0;

    [Column("losses")]
    public int Losses { get; set; } = 0;

    [Column("last_login")]
    public DateTime? LastLogin { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("language")]
    [MaxLength(10)]
    public string Language { get; set; } = "en";
} 