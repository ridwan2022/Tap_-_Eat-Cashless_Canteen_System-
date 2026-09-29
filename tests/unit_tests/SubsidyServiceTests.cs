using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Options;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests;

public class SubsidyServiceTests
{
    private readonly Mock<IWalletRepository> _walletsMock;
    private readonly Mock<IWalletService> _walletServiceMock;
    private readonly Mock<IUserRepository> _usersMock;
    private readonly Mock<TimeProvider> _clockMock;
    private readonly DateTimeOffset _now = new(2026, 9, 20, 4, 0, 0, TimeSpan.Zero);
    private readonly SubsidyService _sut;

    public SubsidyServiceTests()
    {
        _walletsMock = new Mock<IWalletRepository>();
        _walletServiceMock = new Mock<IWalletService>();
        _usersMock = new Mock<IUserRepository>();
        _clockMock = new Mock<TimeProvider>();
        _clockMock.Setup(c => c.GetUtcNow()).Returns(_now);

        _sut = new SubsidyService(
            _walletsMock.Object,
            _walletServiceMock.Object,
            _usersMock.Object,
            Options.Create(new BusinessOptions()),
            _clockMock.Object);
    }

    private static WalletPosting Posting(bool created) =>
        new(new WalletTransactionDto(Guid.NewGuid(), "SubsidyCredit", 100m, 100m, "subsidy", DateTimeOffset.UtcNow), created);

    private SubsidySchedule GivenSchedule(Guid userId, SubsidyFrequency frequency, bool userActive = true, string? lastPeriod = null)
    {
        var schedule = new SubsidySchedule
        {
            UserId = userId,
            Amount = 100m,
            Frequency = frequency,
            LastCreditedPeriodKey = lastPeriod
        };
        _walletsMock.Setup(r => r.GetActiveSchedulesAsync()).ReturnsAsync(new List<SubsidySchedule> { schedule });
        _usersMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(new User { Id = userId, IsActive = userActive });
        return schedule;
    }

    // ============ SetSchedule Tests ============

    [Fact]
    public async Task SetScheduleAsync_WithValidData_SavesSchedule()
    {
        var userId = Guid.NewGuid();
        _usersMock.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(new User { Id = userId });

        var result = await _sut.SetScheduleAsync(userId, 500m, SubsidyFrequency.Weekly, true);

        result.Amount.Should().Be(500m);
        result.Frequency.Should().Be("Weekly");
        result.IsActive.Should().BeTrue();
        _walletsMock.Verify(r => r.UpsertScheduleAsync(
            It.Is<SubsidySchedule>(s => s.UserId == userId && s.Amount == 500m)), Times.Once);
    }

    [Fact]
    public async Task SetScheduleAsync_WithZeroAmount_ThrowsWalletException()
    {
        Func<Task> act = () => _sut.SetScheduleAsync(Guid.NewGuid(), 0m, SubsidyFrequency.Daily, true);

        var ex = await act.Should().ThrowAsync<WalletException>()
            .WithMessage("*greater than zero*");
        ex.Which.Kind.Should().Be(ErrorKind.Invalid);
        _walletsMock.Verify(r => r.UpsertScheduleAsync(It.IsAny<SubsidySchedule>()), Times.Never);
    }

    // ============ RunDueCredits Tests ============

    [Fact]
    public async Task RunDueCreditsAsync_CreditsScheduleForNewPeriod()
    {
        var userId = Guid.NewGuid();
        var schedule = GivenSchedule(userId, SubsidyFrequency.Daily);
        _walletServiceMock
            .Setup(w => w.CreditAsync(userId, 100m, WalletTransactionType.SubsidyCredit,
                It.IsAny<string>(), $"subsidy:{userId:N}:2026-09-20"))
            .ReturnsAsync(Posting(true));

        var result = await _sut.RunDueCreditsAsync();

        result.Credited.Should().Be(1);
        result.AlreadyCredited.Should().Be(0);
        schedule.LastCreditedPeriodKey.Should().Be("2026-09-20");
    }

    [Fact]
    public async Task RunDueCreditsAsync_SkipsScheduleAlreadyCreditedThisPeriod()
    {
        GivenSchedule(Guid.NewGuid(), SubsidyFrequency.Daily, lastPeriod: "2026-09-20");

        var result = await _sut.RunDueCreditsAsync();

        result.Credited.Should().Be(0);
        result.AlreadyCredited.Should().Be(1);
        _walletServiceMock.Verify(w => w.CreditAsync(It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<WalletTransactionType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RunDueCreditsAsync_SkipsInactiveUser()
    {
        GivenSchedule(Guid.NewGuid(), SubsidyFrequency.Daily, userActive: false);

        var result = await _sut.RunDueCreditsAsync();

        result.Credited.Should().Be(0);
        result.AlreadyCredited.Should().Be(0);
        _walletServiceMock.Verify(w => w.CreditAsync(It.IsAny<Guid>(), It.IsAny<decimal>(),
            It.IsAny<WalletTransactionType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RunDueCreditsAsync_WhenLedgerRefusesDuplicate_CountsAsAlreadyCredited()
    {
        var userId = Guid.NewGuid();
        GivenSchedule(userId, SubsidyFrequency.Monthly);
        _walletServiceMock
            .Setup(w => w.CreditAsync(userId, 100m, It.IsAny<WalletTransactionType>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Posting(false));

        var result = await _sut.RunDueCreditsAsync();

        result.Credited.Should().Be(0);
        result.AlreadyCredited.Should().Be(1);
    }

    // ============ PeriodKey Tests ============

    [Fact]
    public void PeriodKey_Weekly_UsesIsoWeekNumber()
    {
        var local = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.FromHours(6));

        var key = SubsidyService.PeriodKey(SubsidyFrequency.Weekly, local);

        key.Should().Be("2026-W38");
    }

    [Fact]
    public void PeriodKey_Weekly_AtYearBoundary_UsesIsoYear()
    {
        var local = new DateTimeOffset(2027, 1, 1, 9, 0, 0, TimeSpan.FromHours(6));

        var key = SubsidyService.PeriodKey(SubsidyFrequency.Weekly, local);

        key.Should().Be("2026-W53");
    }
}