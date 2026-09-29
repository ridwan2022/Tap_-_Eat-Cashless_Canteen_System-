namespace TapAndEat.Api.Models;

/// <summary>
/// Task 1.5 — forgot/reset-password flow. We only ever persist a hash of the
/// token (never the raw value), the same way passwords are hashed, so a
/// leaked datastore can't be used to reset accounts.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string TokenHash { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public bool Used { get; set; }
}
