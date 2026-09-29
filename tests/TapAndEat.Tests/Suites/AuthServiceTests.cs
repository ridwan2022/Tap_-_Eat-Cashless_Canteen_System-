using TapAndEat.Api.DTOs;
using TapAndEat.Api.Services;
using TapAndEat.Tests.Framework;

namespace TapAndEat.Tests.Suites;

/// <summary>Task 1.6 — unit tests for registration, login, and forgot/reset password.</summary>
public static class AuthServiceTests
{
    public static void Register(TestRunner runner)
    {
        const string suite = "AuthService (Task 1.6)";

        runner.Add(suite, "Register creates a Customer account and returns a usable token", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();

            var result = await auth.RegisterAsync(new RegisterRequest("Nusrat Jahan", "nusrat@example.com", "Password123"));

            Assert.NotNull(result.Token);
            Assert.Equal("Customer", result.User.Role);
            Assert.Equal("nusrat@example.com", result.User.Email);
            Assert.NotNull(fixture.TokenService.ValidateToken(result.Token));
        });

        runner.Add(suite, "Register with an already-registered email throws AuthException", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();
            await auth.RegisterAsync(new RegisterRequest("A", "dup@example.com", "Password123"));

            await Assert.ThrowsAsync<AuthException>(() =>
                auth.RegisterAsync(new RegisterRequest("B", "dup@example.com", "AnotherPass123")));
        });

        runner.Add(suite, "Login with correct credentials returns a valid token", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();
            await auth.RegisterAsync(new RegisterRequest("Rifat", "rifat@example.com", "Password123"));

            var result = await auth.LoginAsync(new LoginRequest("rifat@example.com", "Password123"));

            Assert.NotNull(fixture.TokenService.ValidateToken(result.Token));
        });

        runner.Add(suite, "Login with the wrong password throws AuthException", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();
            await auth.RegisterAsync(new RegisterRequest("Rifat", "rifat2@example.com", "Password123"));

            await Assert.ThrowsAsync<AuthException>(() =>
                auth.LoginAsync(new LoginRequest("rifat2@example.com", "WrongPassword1")));
        });

        runner.Add(suite, "Login for an unregistered email fails with the same message as a wrong password (no user enumeration)", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();
            await auth.RegisterAsync(new RegisterRequest("Someone", "known@example.com", "Password123"));

            var unknownEx = await Assert.ThrowsAsync<AuthException>(() =>
                auth.LoginAsync(new LoginRequest("unknown@example.com", "whatever123")));
            var wrongPasswordEx = await Assert.ThrowsAsync<AuthException>(() =>
                auth.LoginAsync(new LoginRequest("known@example.com", "wrong-password")));

            Assert.Equal(wrongPasswordEx.Message, unknownEx.Message);
        });

        runner.Add(suite, "Login for a deactivated account is rejected", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();
            var registered = await auth.RegisterAsync(new RegisterRequest("Deact", "deact@example.com", "Password123"));
            var user = await fixture.Users.GetByIdAsync(registered.User.Id);
            user!.IsActive = false;
            await fixture.Users.UpdateAsync(user);

            await Assert.ThrowsAsync<AuthException>(() =>
                auth.LoginAsync(new LoginRequest("deact@example.com", "Password123")));
        });

        runner.Add(suite, "Forgot-password for an unknown email returns a generic message and no dev token", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();

            var result = await auth.ForgotPasswordAsync(new ForgotPasswordRequest("ghost@example.com"));

            Assert.Null(result.DevOnlyResetToken);
            Assert.NotNull(result.Message);
        });

        runner.Add(suite, "Forgot-password for a known email returns a dev-only reset token", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();
            await auth.RegisterAsync(new RegisterRequest("Has Account", "hasaccount@example.com", "Password123"));

            var result = await auth.ForgotPasswordAsync(new ForgotPasswordRequest("hasaccount@example.com"));

            Assert.NotNull(result.DevOnlyResetToken);
        });

        runner.Add(suite, "Reset-password with a valid token changes the password and revokes old sessions", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();
            var registered = await auth.RegisterAsync(new RegisterRequest("Reset Me", "resetme@example.com", "OldPassword1"));
            var oldToken = registered.Token;

            var forgot = await auth.ForgotPasswordAsync(new ForgotPasswordRequest("resetme@example.com"));
            await auth.ResetPasswordAsync(new ResetPasswordRequest("resetme@example.com", forgot.DevOnlyResetToken!, "NewPassword1"));

            // Old session must now be invalid...
            Assert.Null(fixture.TokenService.ValidateToken(oldToken));
            // ...old password must be rejected...
            await Assert.ThrowsAsync<AuthException>(() =>
                auth.LoginAsync(new LoginRequest("resetme@example.com", "OldPassword1")));
            // ...and the new password must work.
            var loginResult = await auth.LoginAsync(new LoginRequest("resetme@example.com", "NewPassword1"));
            Assert.NotNull(loginResult.Token);
        });

        runner.Add(suite, "Reset-password with an invalid token throws and does not change the password", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();
            await auth.RegisterAsync(new RegisterRequest("Untouched", "untouched@example.com", "OriginalPass1"));

            await Assert.ThrowsAsync<AuthException>(() =>
                auth.ResetPasswordAsync(new ResetPasswordRequest("untouched@example.com", "not-a-real-token", "NewPassword1")));

            var loginResult = await auth.LoginAsync(new LoginRequest("untouched@example.com", "OriginalPass1"));
            Assert.NotNull(loginResult.Token);
        });

        runner.Add(suite, "A reset token can only be used once", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();
            await auth.RegisterAsync(new RegisterRequest("OneShot", "oneshot@example.com", "OldPassword1"));
            var forgot = await auth.ForgotPasswordAsync(new ForgotPasswordRequest("oneshot@example.com"));

            await auth.ResetPasswordAsync(new ResetPasswordRequest("oneshot@example.com", forgot.DevOnlyResetToken!, "NewPassword1"));

            await Assert.ThrowsAsync<AuthException>(() =>
                auth.ResetPasswordAsync(new ResetPasswordRequest("oneshot@example.com", forgot.DevOnlyResetToken!, "AnotherPassword1")));
        });

        runner.Add(suite, "GetCurrentUser (Task 1.3 /me) returns the correct profile", async () =>
        {
            var fixture = new ServiceFixture();
            var auth = fixture.BuildAuthService();
            var registered = await auth.RegisterAsync(new RegisterRequest("Me Profile", "meprofile@example.com", "Password123"));

            var profile = await auth.GetCurrentUserAsync(registered.User.Id);

            Assert.NotNull(profile);
            Assert.Equal("meprofile@example.com", profile!.Email);
        });
    }
}
