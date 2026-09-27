using System.ComponentModel.DataAnnotations;

namespace TapAndEat.Api.DTOs;

public record RegisterRequest(
    [Required, StringLength(100, MinimumLength = 2)] string FullName,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters.")] string Password);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record UserSummary(Guid Id, string FullName, string Email, string Role, bool IsActive);

public record AuthResponse(string Token, DateTimeOffset ExpiresAtUtc, UserSummary User);

public record ForgotPasswordRequest([Required, EmailAddress] string Email);

/// <summary>
/// Sprint 1 has no email/SMS gateway yet, so ForgotPasswordResponse hands the
/// reset token straight back (and it's echoed server-side via ILogger) so the
/// flow is fully testable end-to-end. Sprint 2 should instead email/SMS this
/// token to the user and drop it from the HTTP response.
/// </summary>
public record ForgotPasswordResponse(string Message, string? DevOnlyResetToken);

public record ResetPasswordRequest(
    [Required, EmailAddress] string Email,
    [Required] string Token,
    [Required, MinLength(8)] string NewPassword);
