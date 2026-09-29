using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.AspNetCore.SignalR;
using TapAndEat.Api.Hubs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests;

public class MenuServiceStockTests
{
    private readonly Mock<IMenuRepository> _menuMock;
    private readonly Mock<IHubContext<MenuHub>> _hubMock;
    private readonly Mock<IHubClients> _clientsMock;
    private readonly Mock<IClientProxy> _proxyMock;
    private readonly MenuService _sut;

    public MenuServiceStockTests()
    {
        _menuMock = new Mock<IMenuRepository>();
        _hubMock = new Mock<IHubContext<MenuHub>>();
        _clientsMock = new Mock<IHubClients>();
        _proxyMock = new Mock<IClientProxy>();

        _clientsMock.Setup(c => c.Group(MenuHub.MenuWatchersGroup)).Returns(_proxyMock.Object);
        _hubMock.Setup(h => h.Clients).Returns(_clientsMock.Object);

        _sut = new MenuService(_menuMock.Object, _hubMock.Object);
    }

    private MenuItem GivenItem(int stock, bool published = true, string name = "Biryani")
    {
        var item = new MenuItem { Name = name, StockCount = stock, IsPublished = published };
        _menuMock.Setup(r => r.GetByIdAsync(item.Id)).ReturnsAsync(item);
        return item;
    }

    // ============ ReserveStock Tests ============

    [Fact]
    public async Task ReserveStockAsync_WithEnoughStock_DecrementsStockAndBroadcasts()
    {
        var item = GivenItem(10);

        await _sut.ReserveStockAsync(new List<StockLine> { new(item.Id, 3) });

        item.StockCount.Should().Be(7);
        _menuMock.Verify(r => r.UpdateAsync(item), Times.Once);
        _proxyMock.Verify(p => p.SendCoreAsync("StockUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReserveStockAsync_WithInsufficientStock_ThrowsAndChangesNothing()
    {
        var item = GivenItem(2);

        Func<Task> act = () => _sut.ReserveStockAsync(new List<StockLine> { new(item.Id, 5) });

        await act.Should().ThrowAsync<MenuException>()
            .WithMessage("*Only 2 of Biryani left*");
        item.StockCount.Should().Be(2);
        _menuMock.Verify(r => r.UpdateAsync(It.IsAny<MenuItem>()), Times.Never);
    }

    [Fact]
    public async Task ReserveStockAsync_WithUnpublishedItem_ThrowsMenuException()
    {
        var item = GivenItem(10, published: false);

        Func<Task> act = () => _sut.ReserveStockAsync(new List<StockLine> { new(item.Id, 1) });

        await act.Should().ThrowAsync<MenuException>()
            .WithMessage("*no longer on the menu*");
    }

    [Fact]
    public async Task ReserveStockAsync_WhenSecondLineFails_DoesNotDecrementFirstLine()
    {
        var available = GivenItem(10, name: "Khichuri");
        var soldOut = GivenItem(0, name: "Biryani");

        Func<Task> act = () => _sut.ReserveStockAsync(new List<StockLine>
        {
            new(available.Id, 2),
            new(soldOut.Id, 1)
        });

        await act.Should().ThrowAsync<MenuException>()
            .WithMessage("*out of stock*");
        available.StockCount.Should().Be(10);
        _menuMock.Verify(r => r.UpdateAsync(It.IsAny<MenuItem>()), Times.Never);
    }

    // ============ ReleaseStock Tests ============

    [Fact]
    public async Task ReleaseStockAsync_AddsStockBackAndBroadcasts()
    {
        var item = GivenItem(4);

        await _sut.ReleaseStockAsync(new List<StockLine> { new(item.Id, 3) });

        item.StockCount.Should().Be(7);
        _menuMock.Verify(r => r.UpdateAsync(item), Times.Once);
        _proxyMock.Verify(p => p.SendCoreAsync("StockUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}