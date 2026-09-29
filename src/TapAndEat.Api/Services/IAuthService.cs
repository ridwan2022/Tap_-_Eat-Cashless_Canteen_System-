using TapAndEat.Api.DTOs;

namespace TapAndEat.Api.Services;

public class AuthException : Exception
{
    public AuthException(string message) : base(message) { }
}

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<ForgotPasswordResponse> ForgotPasswordAsync(ForgotPasswordRequest request);
    Task ResetPasswordAsync(ResetPasswordRequest request);
    Task<UserSummary?> GetCurrentUserAsync(Guid userId);
}
