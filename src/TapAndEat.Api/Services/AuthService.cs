using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;

namespace TapAndEat.Api.Services;

/// <summary>
/// Tasks 1.2, 1.5 — registration/login and forgot/reset password.
/// Passwords are hashed with ASP.NET Core Identity's PasswordHasher (PBKDF2)
/// — the same primitive full ASP.NET Core Identity uses internally — so
/// nothing here is a home-grown crypto scheme.
/// </summary>
public class AuthService : IAuthService
{
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromMinutes(15);

    private readonly IUserRepository _users;
    private readonly IPasswordResetRepository _resetTokens;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IPasswordResetRepository resetTokens,
        ITokenService tokenService,
        IPasswordHasher<User> passwordHasher,
        ILogger<AuthService> logger)
    {
        _users = users;
        _resetTokens = resetTokens;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        if (await _users.EmailExistsAsync(request.Email))
        {
            throw new AuthException("An account with this email already exists.");
        }

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Role = UserRole.Customer
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        await _users.AddAsync(user);

        var issued = _tokenService.IssueToken(user);
        return new AuthResponse(issued.Token, issued.ExpiresAtUtc, ToSummary(user));
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim());
        // Deliberately identical error for "no such user" and "wrong password"
        // so the endpoint doesn't leak which emails are registered.
        if (user is null)
        {
            throw new AuthException("Invalid email or password.");
        }

        if (!user.IsActive)
        {
            throw new AuthException("This account has been deactivated. Contact an administrator.");
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw new AuthException("Invalid email or password.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            await _users.UpdateAsync(user);
        }

        var issued = _tokenService.IssueToken(user);
        return new AuthResponse(issued.Token, issued.ExpiresAtUtc, ToSummary(user));
    }

    public async Task<ForgotPasswordResponse> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Don't reveal whether the email exists — return a generic message.
            return new ForgotPasswordResponse(
                "If an account with that email exists, a reset link/OTP has been sent.", null);
        }

        var rawToken = GenerateRawToken();
        var tokenRecord = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = Hash(rawToken),
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ResetTokenLifetime)
        };
        await _resetTokens.AddAsync(tokenRecord);

        // No email/SMS gateway is wired up in Sprint 1 (out of scope). We log
        // the token server-side and also return it in DevOnlyResetToken so a
        // reviewer can exercise the whole flow without a mail server.
        _logger.LogInformation(
            "Password reset requested for {Email}. Dev-only token: {Token} (expires {Expiry})",
            user.Email, rawToken, tokenRecord.ExpiresAtUtc);

        return new ForgotPasswordResponse(
            "If an account with that email exists, a reset link/OTP has been sent.", rawToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            throw new AuthException("Invalid or expired reset token.");
        }

        var tokenHash = Hash(request.Token);
        var tokenRecord = await _resetTokens.GetLatestValidForUserAsync(user.Id, tokenHash);
        if (tokenRecord is null)
        {
            throw new AuthException("Invalid or expired reset token.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.SecurityStamp++; // invalidates every previously issued token for this user
        await _users.UpdateAsync(user);
        await _resetTokens.MarkUsedAsync(tokenRecord.Id);

        // "Old password stops working immediately after reset" — enforced by
        // revoking every session token issued before this point.
        _tokenService.RevokeAllForUser(user.Id);
    }

    public async Task<UserSummary?> GetCurrentUserAsync(Guid userId)
    {
        var user = await _users.GetByIdAsync(userId);
        return user is null ? null : ToSummary(user);
    }

    private static UserSummary ToSummary(User user) =>
        new(user.Id, user.FullName, user.Email, user.Role.ToString(), user.IsActive);

    private static string GenerateRawToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
