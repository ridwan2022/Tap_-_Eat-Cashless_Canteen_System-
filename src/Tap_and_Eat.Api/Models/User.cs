namespace TapAndEat.Api.Models;

/// <summary>
/// Task 1.1 — user account schema: identity, hashed credential, and role.
/// Passwords are never stored or logged in plain text (only PasswordHash is
/// persisted, produced by ASP.NET Core Identity's PasswordHasher).
/// </summary>
public class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Customer;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Bumped every time the password changes or an admin force-revokes
    /// sessions. All previously issued tokens carrying an older stamp are
    /// treated as invalid, which is how "old password stops working
    /// immediately after reset" (Task 1.5 acceptance criteria) is enforced.
    /// </summary>
    public int SecurityStamp { get; set; } = 0;
}