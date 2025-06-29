using System.ComponentModel.DataAnnotations;

namespace w101.Api.DTOs;

public class LoginRequestDto
{
    [Required(ErrorMessage = "Kullanıcı adı gereklidir")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre gereklidir")]
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequestDto
{
    [Required(ErrorMessage = "Kullanıcı adı gereklidir")]
    [StringLength(255, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-255 karakter arasında olmalıdır")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre gereklidir")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifre en az 6 karakter olmalıdır")]
    public string Password { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Geçerli bir email adresi giriniz")]
    public string? Email { get; set; }

    [StringLength(255, ErrorMessage = "Görünen ad 255 karakterden fazla olamaz")]
    public string? DisplayName { get; set; }

    [StringLength(10, ErrorMessage = "Dil kodu 10 karakterden fazla olamaz")]
    public string Language { get; set; } = "tr";
}

public class AuthResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Token { get; set; }
    public UserDto? User { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public int? RankId { get; set; }
    public int Level { get; set; }
    public int Diamonds { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Language { get; set; } = string.Empty;
} 