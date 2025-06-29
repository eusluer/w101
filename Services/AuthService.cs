using Microsoft.EntityFrameworkCore;
using w101.Api.Data;
using w101.Api.DTOs;
using w101.Api.Models;
using BCrypt.Net;

namespace w101.Api.Services;

public interface IAuthService
{
    Task<AuthResponseDto> LoginAsync(LoginRequestDto loginRequest);
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto registerRequest);
    Task<UserDto?> GetUserByIdAsync(int userId);
}

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtService _jwtService;

    public AuthService(ApplicationDbContext context, IJwtService jwtService)
    {
        _context = context;
        _jwtService = jwtService;
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto loginRequest)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == loginRequest.Username);

            if (user == null)
            {
                return new AuthResponseDto
                {
                    Success = false,
                    Message = "Kullanıcı adı veya şifre hatalı"
                };
            }

            if (!BCrypt.Net.BCrypt.Verify(loginRequest.Password, user.PasswordHash))
            {
                return new AuthResponseDto
                {
                    Success = false,
                    Message = "Kullanıcı adı veya şifre hatalı"
                };
            }

            // Son giriş zamanını güncelle
            user.LastLogin = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var token = _jwtService.GenerateToken(user);
            var userDto = MapToUserDto(user);

            return new AuthResponseDto
            {
                Success = true,
                Message = "Giriş başarılı",
                Token = token,
                User = userDto
            };
        }
        catch (Exception ex)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "Giriş sırasında bir hata oluştu: " + ex.Message
            };
        }
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto registerRequest)
    {
        try
        {
            // Kullanıcı adı kontrolü
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == registerRequest.Username);

            if (existingUser != null)
            {
                return new AuthResponseDto
                {
                    Success = false,
                    Message = "Bu kullanıcı adı zaten kullanılıyor"
                };
            }

            // Email kontrolü (eğer verilmişse)
            if (!string.IsNullOrEmpty(registerRequest.Email))
            {
                var existingEmail = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == registerRequest.Email);

                if (existingEmail != null)
                {
                    return new AuthResponseDto
                    {
                        Success = false,
                        Message = "Bu email adresi zaten kullanılıyor"
                    };
                }
            }

            // Şifreyi hashle
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(registerRequest.Password);

            var newUser = new User
            {
                Username = registerRequest.Username,
                PasswordHash = passwordHash,
                Email = registerRequest.Email,
                DisplayName = registerRequest.DisplayName ?? registerRequest.Username,
                Language = registerRequest.Language,
                Level = 1,
                Diamonds = 0,
                Wins = 0,
                Losses = 0,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            var token = _jwtService.GenerateToken(newUser);
            var userDto = MapToUserDto(newUser);

            return new AuthResponseDto
            {
                Success = true,
                Message = "Kayıt başarılı",
                Token = token,
                User = userDto
            };
        }
        catch (Exception ex)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "Kayıt sırasında bir hata oluştu: " + ex.Message
            };
        }
    }

    public async Task<UserDto?> GetUserByIdAsync(int userId)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            return user != null ? MapToUserDto(user) : null;
        }
        catch
        {
            return null;
        }
    }

    private static UserDto MapToUserDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            RankId = user.RankId,
            Level = user.Level,
            Diamonds = user.Diamonds,
            Wins = user.Wins,
            Losses = user.Losses,
            LastLogin = user.LastLogin,
            CreatedAt = user.CreatedAt,
            Language = user.Language
        };
    }
} 