using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _usersMock;
    private readonly Mock<IPasswordResetRepository> _resetTokensMock;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<IPasswordHasher<User>> _passwordHasherMock;
    private readonly Mock<ILogger<AuthService>> _loggerMock;
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _usersMock = new Mock<IUserRepository>();
        _resetTokensMock = new Mock<IPasswordResetRepository>();
        _tokenServiceMock = new Mock<ITokenService>();
        _passwordHasherMock = new Mock<IPasswordHasher<User>>();
        _loggerMock = new Mock<ILogger<AuthService>>();

        _sut = new AuthService(
            _usersMock.Object,
            _resetTokensMock.Object,
            _tokenServiceMock.Object,
            _passwordHasherMock.Object,
            _loggerMock.Object);
    }

    // ============ Register Tests ============

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ThrowsAuthException()
    {
        var request = new RegisterRequest("Test User", "existing@test.com", "Password123");
        _usersMock.Setup(r => r.EmailExistsAsync(request.Email)).ReturnsAsync(true);

        Func<Task> act = () => _sut.RegisterAsync(request);

        await act.Should().ThrowAsync<AuthException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task RegisterAsync_WithNewEmail_CreatesUserAndReturnsToken()
    {
        var request = new RegisterRequest("Test User", "new@test.com", "Password123");

        _usersMock.Setup(r => r.EmailExistsAsync(request.Email)).ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(It.IsAny<User>(), request.Password))
            .Returns("hashed_password");
        _tokenServiceMock
            .Setup(t => t.IssueToken(It.IsAny<User>()))
            .Returns(new IssuedToken("fake-token", DateTimeOffset.UtcNow.AddHours(8)));

        var result = await _sut.RegisterAsync(request);

        result.Should().NotBeNull();
        result.Token.Should().Be("fake-token");
        result.User.Email.Should().Be("new@test.com");
        _usersMock.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Once);
    }

    // ============ Login Tests ============

    [Fact]
    public async Task LoginAsync_WithNonExistentUser_ThrowsAuthException()
    {
        var request = new LoginRequest("ghost@test.com", "Password123");
        _usersMock.Setup(r => r.GetByEmailAsync(request.Email)).ReturnsAsync((User?)null);

        Func<Task> act = () => _sut.LoginAsync(request);

        await act.Should().ThrowAsync<AuthException>()
            .WithMessage("*Invalid email or password*");
    }

    [Fact]
    public async Task LoginAsync_WithInactiveUser_ThrowsAuthException()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "inactive@test.com", PasswordHash = "h", IsActive = false };
        var request = new LoginRequest("inactive@test.com", "Password123");

        _usersMock.Setup(r => r.GetByEmailAsync(request.Email)).ReturnsAsync(user);

        Func<Task> act = () => _sut.LoginAsync(request);

        await act.Should().ThrowAsync<AuthException>()
            .WithMessage("*deactivated*");
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsAuthException()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "test@test.com", PasswordHash = "h", IsActive = true };
        var request = new LoginRequest("test@test.com", "WrongPass");

        _usersMock.Setup(r => r.GetByEmailAsync(request.Email)).ReturnsAsync(user);
        _passwordHasherMock
            .Setup(h => h.VerifyHashedPassword(user, user.PasswordHash, request.Password))
            .Returns(PasswordVerificationResult.Failed);

        Func<Task> act = () => _sut.LoginAsync(request);

        await act.Should().ThrowAsync<AuthException>();
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsToken()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "test@test.com", PasswordHash = "h", IsActive = true };
        var request = new LoginRequest("test@test.com", "Password123");

        _usersMock.Setup(r => r.GetByEmailAsync(request.Email)).ReturnsAsync(user);
        _passwordHasherMock
            .Setup(h => h.VerifyHashedPassword(user, user.PasswordHash, request.Password))
            .Returns(PasswordVerificationResult.Success);
        _tokenServiceMock
            .Setup(t => t.IssueToken(user))
            .Returns(new IssuedToken("valid-token", DateTimeOffset.UtcNow.AddHours(8)));

        var result = await _sut.LoginAsync(request);

        result.Token.Should().Be("valid-token");
    }

    // ============ Forgot Password Tests ============

    [Fact]
    public async Task ForgotPasswordAsync_WithNonExistentEmail_ReturnsGenericMessage()
    {
        _usersMock.Setup(r => r.GetByEmailAsync("ghost@test.com")).ReturnsAsync((User?)null);
        var request = new ForgotPasswordRequest("ghost@test.com");

        var result = await _sut.ForgotPasswordAsync(request);

        result.Message.Should().Contain("If an account");
        result.DevOnlyResetToken.Should().BeNull();
    }

    [Fact]
    public async Task ForgotPasswordAsync_WithValidEmail_ReturnsResetToken()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "test@test.com", IsActive = true };
        _usersMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        var result = await _sut.ForgotPasswordAsync(new ForgotPasswordRequest(user.Email));

        result.DevOnlyResetToken.Should().NotBeNullOrEmpty();
        _resetTokensMock.Verify(r => r.AddAsync(It.IsAny<PasswordResetToken>()), Times.Once);
    }

    // ============ Reset Password Tests ============

    [Fact]
    public async Task ResetPasswordAsync_WithInvalidToken_ThrowsAuthException()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "test@test.com", IsActive = true };
        _usersMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _resetTokensMock
            .Setup(r => r.GetLatestValidForUserAsync(user.Id, It.IsAny<string>()))
            .ReturnsAsync((PasswordResetToken?)null);

        var request = new ResetPasswordRequest(user.Email, "bad-token", "NewPassword123");
        Func<Task> act = () => _sut.ResetPasswordAsync(request);

        await act.Should().ThrowAsync<AuthException>()
            .WithMessage("*Invalid or expired*");
    }

    [Fact]
    public async Task ResetPasswordAsync_WithValidToken_UpdatesPasswordAndRevokesTokens()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "test@test.com", PasswordHash = "old", IsActive = true };
        var tokenRecord = new PasswordResetToken { Id = Guid.NewGuid(), UserId = user.Id };

        _usersMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _resetTokensMock
            .Setup(r => r.GetLatestValidForUserAsync(user.Id, It.IsAny<string>()))
            .ReturnsAsync(tokenRecord);
        _passwordHasherMock
            .Setup(h => h.HashPassword(user, "NewPassword123"))
            .Returns("new-hash");

        var request = new ResetPasswordRequest(user.Email, "valid-token", "NewPassword123");
        await _sut.ResetPasswordAsync(request);

        user.PasswordHash.Should().Be("new-hash");
        _usersMock.Verify(r => r.UpdateAsync(user), Times.Once);
        _resetTokensMock.Verify(r => r.MarkUsedAsync(tokenRecord.Id), Times.Once);
        _tokenServiceMock.Verify(t => t.RevokeAllForUser(user.Id), Times.Once);
    }

    // ============ GetCurrentUser Tests ============

    [Fact]
    public async Task GetCurrentUserAsync_WithValidId_ReturnsSummary()
    {
        var user = new User { Id = Guid.NewGuid(), FullName = "Test", Email = "t@t.com", IsActive = true };
        _usersMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var result = await _sut.GetCurrentUserAsync(user.Id);

        result.Should().NotBeNull();
        result!.Email.Should().Be("t@t.com");
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithInvalidId_ReturnsNull()
    {
        _usersMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

        var result = await _sut.GetCurrentUserAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}