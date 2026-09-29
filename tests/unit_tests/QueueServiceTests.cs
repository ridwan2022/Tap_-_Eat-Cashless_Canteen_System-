using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Options;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests;

public class QueueServiceTests
{
    private readonly Mock<IQueueTokenRepository> _tokensMock;
    private readonly Mock<IOrderRepository> _ordersMock;
    private readonly Mock<IEventBroadcaster> _eventsMock;
    private readonly Mock<TimeProvider> _clockMock;
    private readonly DateTimeOffset _now = new(2026, 9, 20, 4, 0, 0, TimeSpan.Zero);
    private readonly QueueService _sut;

    public QueueServiceTests()
    {
        _tokensMock = new Mock<IQueueTokenRepository>();
        _ordersMock = new Mock<IOrderRepository>();
        _eventsMock = new Mock<IEventBroadcaster>();
        _clockMock = new Mock<TimeProvider>();
        _clockMock.Setup(c => c.GetUtcNow()).Returns(_now);

        _tokensMock.Setup(r => r.GetActiveAsync()).ReturnsAsync(new List<QueueToken>());

        _sut = new QueueService(
            _tokensMock.Object,
            _ordersMock.Object,
            _eventsMock.Object,
            Options.Create(new KitchenOptions { Stations = 2 }),
            Options.Create(new BusinessOptions()),
            _clockMock.Object);
    }

    private static Order OrderWith(int quantity, int prepMinutes) => new()
    {
        Lines = { new OrderLine { MenuItemId = Guid.NewGuid(), Name = "Item", UnitPrice = 100m, Quantity = quantity, PrepTimeMinutes = prepMinutes } }
    };

    // Makes the repository run the service's factory with the given token number.
    private void GivenNextTokenNumber(int number) =>
        _tokensMock
            .Setup(r => r.AddForOrderAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Func<int, QueueToken>>()))
            .Returns((Guid orderId, string dayKey, Func<int, QueueToken> factory) => Task.FromResult(factory(number)));

    private QueueToken GivenToken(QueueTokenStatus status)
    {
        var token = new QueueToken { OrderId = Guid.NewGuid(), TokenNumber = 3, Status = status };
        _tokensMock.Setup(r => r.GetByIdAsync(token.Id)).ReturnsAsync(token);
        return token;
    }

    // ============ EnsureToken Tests ============

    [Fact]
    public async Task EnsureTokenForOrderAsync_IssuesTokenWithEstimate()
    {
        GivenNextTokenNumber(7);
        var order = OrderWith(quantity: 2, prepMinutes: 10);

        var token = await _sut.EnsureTokenForOrderAsync(order);

        token.TokenNumber.Should().Be(7);
        token.Label.Should().Be("T-007");
        token.DayKey.Should().Be("2026-09-20");
        token.WorkMinutes.Should().Be(20);
        token.OwnMinutes.Should().Be(10);
        token.EstimatedPrepMinutes.Should().Be(10);
        token.Status.Should().Be(QueueTokenStatus.Queued);
        _eventsMock.Verify(e => e.Publish(It.Is<ServerEvent>(ev => ev.Type == "token")), Times.Once);
    }

    [Fact]
    public async Task EnsureTokenForOrderAsync_WithWorkAhead_AddsWaitAndIgnoresReadyTokens()
    {
        GivenNextTokenNumber(8);
        _tokensMock.Setup(r => r.GetActiveAsync()).ReturnsAsync(new List<QueueToken>
        {
            new() { WorkMinutes = 10, Status = QueueTokenStatus.Queued },
            new() { WorkMinutes = 20, Status = QueueTokenStatus.Preparing },
            new() { WorkMinutes = 40, Status = QueueTokenStatus.Ready }   // already cooked, not counted
        });
        var order = OrderWith(quantity: 1, prepMinutes: 5);

        var token = await _sut.EnsureTokenForOrderAsync(order);

        // work ahead 30 min / 2 stations = 15, plus its own 5 min
        token.EstimatedPrepMinutes.Should().Be(20);
    }

    // ============ SetStatus Tests ============

    [Fact]
    public async Task SetStatusAsync_WithUnknownToken_ThrowsNotFound()
    {
        Func<Task> act = () => _sut.SetStatusAsync(Guid.NewGuid(), QueueTokenStatus.Preparing);

        var ex = await act.Should().ThrowAsync<QueueException>()
            .WithMessage("*Token not found*");
        ex.Which.Kind.Should().Be(ErrorKind.NotFound);
    }

    [Fact]
    public async Task SetStatusAsync_FromQueuedToPreparing_SetsStartTimeAndPublishesEvent()
    {
        var token = GivenToken(QueueTokenStatus.Queued);

        var result = await _sut.SetStatusAsync(token.Id, QueueTokenStatus.Preparing);

        result.Status.Should().Be("Preparing");
        token.StartedAtUtc.Should().Be(_now);
        _tokensMock.Verify(r => r.UpdateAsync(token), Times.Once);
        _eventsMock.Verify(e => e.Publish(It.Is<ServerEvent>(ev => ev.Type == "token")), Times.Once);
    }

    [Fact]
    public async Task SetStatusAsync_MovingBackwards_ThrowsConflict()
    {
        var token = GivenToken(QueueTokenStatus.Ready);

        Func<Task> act = () => _sut.SetStatusAsync(token.Id, QueueTokenStatus.Preparing);

        var ex = await act.Should().ThrowAsync<QueueException>()
            .WithMessage("*go back*");
        ex.Which.Kind.Should().Be(ErrorKind.Conflict);
        _tokensMock.Verify(r => r.UpdateAsync(It.IsAny<QueueToken>()), Times.Never);
    }

    [Fact]
    public async Task SetStatusAsync_ToCollected_ThrowsInvalid()
    {
        var token = GivenToken(QueueTokenStatus.Ready);

        Func<Task> act = () => _sut.SetStatusAsync(token.Id, QueueTokenStatus.Collected);

        var ex = await act.Should().ThrowAsync<QueueException>()
            .WithMessage("*collected at the counter*");
        ex.Which.Kind.Should().Be(ErrorKind.Invalid);
    }

    // ============ MarkCollected Tests ============

    [Fact]
    public async Task MarkCollectedAsync_SetsCollectedAndPublishesEvent()
    {
        var token = new QueueToken { OrderId = Guid.NewGuid(), TokenNumber = 5, Status = QueueTokenStatus.Ready };
        _tokensMock.Setup(r => r.GetByOrderIdAsync(token.OrderId)).ReturnsAsync(token);

        await _sut.MarkCollectedAsync(token.OrderId);

        token.Status.Should().Be(QueueTokenStatus.Collected);
        token.CollectedAtUtc.Should().Be(_now);
        _tokensMock.Verify(r => r.UpdateAsync(token), Times.Once);
        _eventsMock.Verify(e => e.Publish(It.IsAny<ServerEvent>()), Times.Once);
    }

    // ============ PrepTimeEstimator Tests ============

    [Fact]
    public void Estimate_DividesWorkAheadAcrossStations()
    {
        var result = PrepTimeEstimator.Estimate(workAheadMinutes: 30, ownMinutes: 5, stations: 2);

        result.Should().Be(20);
    }

    [Fact]
    public void Estimate_RoundsWaitUp()
    {
        // ceil(5 / 2) = 3, plus 4 minutes of its own
        var result = PrepTimeEstimator.Estimate(workAheadMinutes: 5, ownMinutes: 4, stations: 2);

        result.Should().Be(7);
    }

    [Fact]
    public void Estimate_NeverReturnsLessThanOneMinute()
    {
        var result = PrepTimeEstimator.Estimate(workAheadMinutes: 0, ownMinutes: 0, stations: 0);

        result.Should().Be(1);
    }
}
