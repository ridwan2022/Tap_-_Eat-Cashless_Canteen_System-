using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.AspNetCore.Identity;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;


namespace TapAndEat.Tests;


public class AdminServiceTests
{
    private readonly Mock<IUserRepository> _usersMock;
    private readonly Mock<IMenuRepository> _menuMock;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<IPasswordHasher<User>> _passwordHasherMock;
    private readonly AdminService _sut;


    public AdminServiceTests()
    {
        _usersMock = new Mock<IUserRepository>();
        _menuMock = new Mock<IMenuRepository>();
        _tokenServiceMock = new Mock<ITokenService>();
        _passwordHasherMock = new Mock<IPasswordHasher<User>>();


        _sut = new AdminService(
            _usersMock.Object,
            _menuMock.Object,
            _tokenServiceMock.Object,
            _passwordHasherMock.Object);
    }


    // ============ GetUsers Tests ============


    [Fact]
    public async Task GetUsersAsync_ReturnsAllUsers()
    {
        var users = new List<User>
        {
            new() { Id = Guid.NewGuid(), FullName = "Alice", Email = "alice@test.com", Role = UserRole.Customer, IsActive = true },
            new() { Id = Guid.NewGuid(), FullName = "Bob", Email = "bob@test.com", Role = UserRole.Admin, IsActive = true }
        };
        _usersMock.Setup(r => r.GetAllAsync()).ReturnsAsync(users);


        var result = await _sut.GetUsersAsync();


        result.Should().HaveCount(2);
    }


    // ============ CreateUser Tests ============


    [Fact]
    public async Task CreateUserAsync_WithExistingEmail_ThrowsAdminException()
    {
        var request = new CreateUserByAdminRequest("Dup", "existing@test.com", "Password123", "Customer");
        _usersMock.Setup(r => r.EmailExistsAsync(request.Email)).ReturnsAsync(true);


        Func<Task> act = () => _sut.CreateUserAsync(request);


        await act.Should().ThrowAsync<AdminException>()
            .WithMessage("*already exists*");
    }


    [Fact]
    public async Task CreateUserAsync_WithInvalidRole_ThrowsAdminException()
    {
        var request = new CreateUserByAdminRequest("Test", "test@test.com", "Password123", "SuperHero");
        _usersMock.Setup(r => r.EmailExistsAsync(request.Email)).ReturnsAsync(false);


        Func<Task> act = () => _sut.CreateUserAsync(request);


        await act.Should().ThrowAsync<AdminException>()
            .WithMessage("*not a valid role*");
    }


    [Fact]
    public async Task CreateUserAsync_WithValidData_CreatesUser()
    {
        var request = new CreateUserByAdminRequest("New User", "new@test.com", "Password123", "Customer");
        _usersMock.Setup(r => r.EmailExistsAsync(request.Email)).ReturnsAsync(false);
        _passwordHasherMock
            .Setup(h => h.HashPassword(It.IsAny<User>(), request.Password))
            .Returns("hashed");


        var result = await _sut.CreateUserAsync(request);


        result.Email.Should().Be("new@test.com");
        result.Role.Should().Be("Customer");
        _usersMock.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Once);
    }


    // ============ AssignRole Tests ============


    [Fact]
    public async Task AssignRoleAsync_WithInvalidUser_ThrowsAdminException()
    {
        _usersMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);


        Func<Task> act = () => _sut.AssignRoleAsync(Guid.NewGuid(), "Admin");


        await act.Should().ThrowAsync<AdminException>()
            .WithMessage("*User not found*");
    }


    [Fact]
    public async Task AssignRoleAsync_WithValidUser_UpdatesRoleAndRevokesTokens()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "test@test.com", Role = UserRole.Customer };
        _usersMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);


        var result = await _sut.AssignRoleAsync(userId, "Admin");


        result.Role.Should().Be("Admin");
        _usersMock.Verify(r => r.UpdateAsync(user), Times.Once);
        _tokenServiceMock.Verify(t => t.RevokeAllForUser(userId), Times.Once);
    }


    // ============ SetActive Tests ============


    [Fact]
    public async Task SetActiveAsync_ToFalse_RevokesTokens()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "test@test.com", IsActive = true };
        _usersMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);


        var result = await _sut.SetActiveAsync(userId, false);


        result.IsActive.Should().BeFalse();
        _tokenServiceMock.Verify(t => t.RevokeAllForUser(userId), Times.Once);
    }


    [Fact]
    public async Task SetActiveAsync_ToTrue_DoesNotRevokeTokens()
    {
        var userId = Guid.NewGuid();
        var user = new User { Id = userId, Email = "test@test.com", IsActive = false };
        _usersMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(user);


        await _sut.SetActiveAsync(userId, true);


        _tokenServiceMock.Verify(t => t.RevokeAllForUser(It.IsAny<Guid>()), Times.Never);
    }


    // ============ CreateMenuItem Tests ============


    [Fact]
    public async Task CreateMenuItemAsync_CreatesDraftItem()
    {
        var request = new CreateMenuItemRequest("Biryani", "Tasty", 120, "Main", new List<string> { "Halal" }, 10);


        var result = await _sut.CreateMenuItemAsync(request);


        result.Should().NotBeNull();
        result.Name.Should().Be("Biryani");
        result.IsPublished.Should().BeFalse();
        _menuMock.Verify(r => r.AddAsync(It.IsAny<MenuItem>()), Times.Once);
    }


    [Fact]
    public async Task CreateMenuItemAsync_WithEmptyCategory_UsesGeneral()
    {
        var request = new CreateMenuItemRequest("Item", "Desc", 100, "", null, 5);


        var result = await _sut.CreateMenuItemAsync(request);


        result.Category.Should().Be("General");
    }


    // ============ DeleteMenuItem Tests ============


    [Fact]
    public async Task DeleteMenuItemAsync_WithValidId_DeletesItem()
    {
        var id = Guid.NewGuid();
        _menuMock.Setup(r => r.DeleteAsync(id)).ReturnsAsync(true);


        await _sut.DeleteMenuItemAsync(id);


        _menuMock.Verify(r => r.DeleteAsync(id), Times.Once);
    }


    [Fact]
    public async Task DeleteMenuItemAsync_WithInvalidId_ThrowsAdminException()
    {
        var id = Guid.NewGuid();
        _menuMock.Setup(r => r.DeleteAsync(id)).ReturnsAsync(false);


        Func<Task> act = () => _sut.DeleteMenuItemAsync(id);


        await act.Should().ThrowAsync<AdminException>()
            .WithMessage("*not found*");
    }


    // ============ Override Tests ============


    [Fact]
    public async Task ApplyOverrideAsync_WithValidItem_PublishesAndMarksOverride()
    {
        var id = Guid.NewGuid();
        var item = new MenuItem
        {
            Id = id,
            Name = "Original",
            Price = 100,
            Category = "General",
            IsPublished = false,
            DietaryTags = new List<string>()
        };
        _menuMock.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(item);


        var request = new OverrideMenuItemRequest("Special", "Today", 150, "Special", new List<string> { "Halal" }, 20);


        var result = await _sut.ApplyOverrideAsync(id, request);


        result.IsPublished.Should().BeTrue();
        result.IsOverride.Should().BeTrue();
        result.Name.Should().Be("Special");
    }


    [Fact]
    public async Task ApplyOverrideAsync_WithInvalidId_ThrowsAdminException()
    {
        _menuMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((MenuItem?)null);


        var request = new OverrideMenuItemRequest("X", "Y", 100, "Z", null, 5);


        Func<Task> act = () => _sut.ApplyOverrideAsync(Guid.NewGuid(), request);


        await act.Should().ThrowAsync<AdminException>()
            .WithMessage("*not found*");
    }
}