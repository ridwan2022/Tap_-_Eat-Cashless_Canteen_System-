using Xunit;
using FluentAssertions;
using Moq;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests;

public class WalletServiceTests
{
    private readonly Mock<IWalletRepository> _walletsMock;
    private readonly Mock<IUserRepository> _usersMock;
    private readonly Mock<TimeProvider> _clockMock;
    private readonly DateTimeOffset _now = new(2026, 9, 20, 4, 0, 0, TimeSpan.Zero);
    private readonly Guid _userId;
    private readonly Wallet _wallet;
    private readonly WalletService _sut;

    public WalletServiceTests()
    {
        _walletsMock = new Mock<IWalletRepository>();
        _usersMock = new Mock<IUserRepository>();
        _clockMock = new Mock<TimeProvider>();
        _clockMock.Setup(c => c.GetUtcNow()).Returns(_now);
        _userId = Guid.NewGuid();
        _wallet = new Wallet { UserId = _userId, Balance = 100m };

        _walletsMock.Setup(r => r.GetOrCreateAsync(_userId)).ReturnsAsync(_wallet);

        _sut = new WalletService(_walletsMock.Object, _usersMock.Object, _clockMock.Object);
    }

    // ============ GetWallet Tests ============

    [Fact]
    public async Task GetWalletAsync_ReturnsBalanceAndLinkedCard()
    {
        _wallet.RfidCardUid = "04A1B2C3";

        var result = await _sut.GetWalletAsync(_userId);

        result.Balance.Should().Be(100m);
        result.RfidCardUid.Should().Be("04A1B2C3");
        result.Subsidy.Should().BeNull();
    }

    // ============ Credit Tests ============

    [Fact]
    public async Task CreditAsync_WithValidAmount_IncreasesBalanceAndRecordsTransaction()
    {
        var result = await _sut.CreditAsync(_userId, 50.50m, WalletTransactionType.TopUp, "top-up", "key-1");

        result.Created.Should().BeTrue();
        _wallet.Balance.Should().Be(150.50m);
        result.Transaction.Amount.Should().Be(50.50m);
        result.Transaction.BalanceAfter.Should().Be(150.50m);
        _walletsMock.Verify(r => r.UpdateAsync(_wallet), Times.Once);
        _walletsMock.Verify(r => r.AddTransactionAsync(
            It.Is<WalletTransaction>(t => t.Amount == 50.50m && t.IdempotencyKey == "key-1")), Times.Once);
    }

    [Fact]
    public async Task CreditAsync_WithZeroAmount_ThrowsWalletException()
    {
        Func<Task> act = () => _sut.CreditAsync(_userId, 0m, WalletTransactionType.TopUp, "x", "key-0");

        var ex = await act.Should().ThrowAsync<WalletException>()
            .WithMessage("*greater than zero*");
        ex.Which.Kind.Should().Be(ErrorKind.Invalid);
        _wallet.Balance.Should().Be(100m);
    }

    [Fact]
    public async Task CreditAsync_WithThreeDecimalPlaces_ThrowsWalletException()
    {
        Func<Task> act = () => _sut.CreditAsync(_userId, 10.999m, WalletTransactionType.TopUp, "x", "key-d");

        var ex = await act.Should().ThrowAsync<WalletException>()
            .WithMessage("*2 decimal places*");
        ex.Which.Kind.Should().Be(ErrorKind.Invalid);
    }

    [Fact]
    public async Task CreditAsync_WithExistingIdempotencyKey_DoesNotPostAgain()
    {
        var existing = new WalletTransaction
        {
            WalletId = _wallet.Id,
            Type = WalletTransactionType.TopUp,
            Amount = 100m,
            BalanceAfter = 100m,
            IdempotencyKey = "dup"
        };
        _walletsMock.Setup(r => r.GetTransactionByKeyAsync("dup")).ReturnsAsync(existing);

        var result = await _sut.CreditAsync(_userId, 100m, WalletTransactionType.TopUp, "x", "dup");

        result.Created.Should().BeFalse();
        result.Transaction.Id.Should().Be(existing.Id);
        _wallet.Balance.Should().Be(100m);
        _walletsMock.Verify(r => r.UpdateAsync(It.IsAny<Wallet>()), Times.Never);
        _walletsMock.Verify(r => r.AddTransactionAsync(It.IsAny<WalletTransaction>()), Times.Never);
    }

    // ============ Debit Tests ============

    [Fact]
    public async Task DebitAsync_WithSufficientBalance_DecreasesBalance()
    {
        var result = await _sut.DebitAsync(_userId, 40m, WalletTransactionType.Purchase, "order", "debit-1");

        result.Created.Should().BeTrue();
        _wallet.Balance.Should().Be(60m);
        result.Transaction.Amount.Should().Be(-40m);
        result.Transaction.BalanceAfter.Should().Be(60m);
    }

    [Fact]
    public async Task DebitAsync_WithInsufficientBalance_ThrowsAndLeavesBalanceUnchanged()
    {
        Func<Task> act = () => _sut.DebitAsync(_userId, 150m, WalletTransactionType.Purchase, "order", "debit-2");

        var ex = await act.Should().ThrowAsync<WalletException>()
            .WithMessage("*Insufficient*");
        ex.Which.Kind.Should().Be(ErrorKind.Invalid);
        _wallet.Balance.Should().Be(100m);
        _walletsMock.Verify(r => r.UpdateAsync(It.IsAny<Wallet>()), Times.Never);
    }

    // ============ LinkCard Tests ============

    [Fact]
    public async Task LinkCardAsync_WithInvalidUid_ThrowsWalletException()
    {
        Func<Task> act = () => _sut.LinkCardAsync(_userId, "XYZ");

        var ex = await act.Should().ThrowAsync<WalletException>()
            .WithMessage("*hexadecimal*");
        ex.Which.Kind.Should().Be(ErrorKind.Invalid);
    }

    [Fact]
    public async Task LinkCardAsync_WithFreeCard_LinksNormalizedUid()
    {
        _usersMock.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync(new User { Id = _userId, FullName = "Rahim" });
        _walletsMock.Setup(r => r.TryLinkCardAsync(_wallet.Id, "04A1B2C3")).ReturnsAsync(true);

        var result = await _sut.LinkCardAsync(_userId, "04:a1:b2:c3");

        result.Should().NotBeNull();
        _walletsMock.Verify(r => r.TryLinkCardAsync(_wallet.Id, "04A1B2C3"), Times.Once);
    }

    [Fact]
    public async Task LinkCardAsync_WhenCardBelongsToAnotherWallet_ThrowsConflict()
    {
        _usersMock.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync(new User { Id = _userId });
        _walletsMock.Setup(r => r.TryLinkCardAsync(_wallet.Id, "04A1B2C3")).ReturnsAsync(false);

        Func<Task> act = () => _sut.LinkCardAsync(_userId, "04A1B2C3");

        var ex = await act.Should().ThrowAsync<WalletException>()
            .WithMessage("*another wallet*");
        ex.Which.Kind.Should().Be(ErrorKind.Conflict);
    }
}