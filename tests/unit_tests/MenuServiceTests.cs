using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.AspNetCore.SignalR;
using TapAndEat.Api.Hubs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests;

public class MenuServiceTests
{
    private readonly Mock<IMenuRepository> _menuMock;
    private readonly Mock<IHubContext<MenuHub>> _hubMock;
    private readonly MenuService _sut;

    public MenuServiceTests()
    {
        _menuMock = new Mock<IMenuRepository>();
        _hubMock = new Mock<IHubContext<MenuHub>>();
        _sut = new MenuService(_menuMock.Object, _hubMock.Object);
    }

    // ============ GetPublishedMenu Tests ============

    [Fact]
    public async Task GetPublishedMenuAsync_ReturnsAllPublishedItems()
    {
        var items = new List<MenuItem>
        {
            new() { Id = Guid.NewGuid(), Name = "Biryani", Price = 120, IsPublished = true, DietaryTags = new List<string>() },
            new() { Id = Guid.NewGuid(), Name = "Kacchi", Price = 200, IsPublished = true, DietaryTags = new List<string>() }
        };

        _menuMock.Setup(r => r.GetPublishedAsync(null)).ReturnsAsync(items);

        var result = await _sut.GetPublishedMenuAsync(null);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPublishedMenuAsync_WithTag_FiltersByTag()
    {
        var items = new List<MenuItem>
        {
            new() { Id = Guid.NewGuid(), Name = "Veg", Price = 80, IsPublished = true, DietaryTags = new List<string> { "Vegetarian" } }
        };

        _menuMock.Setup(r => r.GetPublishedAsync("Vegetarian")).ReturnsAsync(items);

        var result = await _sut.GetPublishedMenuAsync("Vegetarian");

        result.Should().HaveCount(1);
        _menuMock.Verify(r => r.GetPublishedAsync("Vegetarian"), Times.Once);
    }

    [Fact]
    public async Task GetPublishedMenuAsync_WhenNoItems_ReturnsEmptyList()
    {
        _menuMock.Setup(r => r.GetPublishedAsync(null)).ReturnsAsync(new List<MenuItem>());

        var result = await _sut.GetPublishedMenuAsync(null);

        result.Should().BeEmpty();
    }

    // ============ GetById Tests ============

    [Fact]
    public async Task GetByIdAsync_WithPublishedItem_ReturnsDto()
    {
        var id = Guid.NewGuid();
        var item = new MenuItem { Id = id, Name = "Biryani", Price = 120, IsPublished = true, DietaryTags = new List<string>() };

        _menuMock.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(item);

        var result = await _sut.GetByIdAsync(id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Biryani");
    }

    [Fact]
    public async Task GetByIdAsync_WithUnpublishedItem_ReturnsNull()
    {
        var id = Guid.NewGuid();
        var item = new MenuItem { Id = id, Name = "Draft", Price = 100, IsPublished = false, DietaryTags = new List<string>() };

        _menuMock.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(item);

        var result = await _sut.GetByIdAsync(id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithInvalidId_ReturnsNull()
    {
        _menuMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((MenuItem?)null);

        var result = await _sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ============ SetStock Tests ============

    [Fact]
    public async Task SetStockAsync_WithNegativeStock_ThrowsMenuException()
    {
        Func<Task> act = () => _sut.SetStockAsync(Guid.NewGuid(), -5);

        await act.Should().ThrowAsync<MenuException>()
            .WithMessage("*cannot be negative*");
    }

    [Fact]
    public async Task SetStockAsync_WithInvalidId_ThrowsMenuException()
    {
        var id = Guid.NewGuid();
        _menuMock.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((MenuItem?)null);

        Func<Task> act = () => _sut.SetStockAsync(id, 10);

        await act.Should().ThrowAsync<MenuException>()
            .WithMessage("*not found*");
    }
}