using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Options;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests;

public class PaymentTokenServiceTests
{
    private readonly PaymentTokenService _sut;

    public PaymentTokenServiceTests()
    {
        _sut = Create("unit-test-secret");
    }

    private static PaymentTokenService Create(string secret) =>
        new(Options.Create(new PaymentOptions { TokenSecret = secret }));

    // ============ Issue / Validate Tests ============

    [Fact]
    public void Issue_ThenTryValidate_ReturnsSameOrderAndPaymentIds()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var token = _sut.Issue(orderId, paymentId);
        var valid = _sut.TryValidate(token, out var parsedOrder, out var parsedPayment);

        valid.Should().BeTrue();
        parsedOrder.Should().Be(orderId);
        parsedPayment.Should().Be(paymentId);
    }

    [Fact]
    public void TryValidate_WithTamperedSignature_ReturnsFalse()
    {
        var token = _sut.Issue(Guid.NewGuid(), Guid.NewGuid());
        var tampered = token[..^1] + (token[^1] == '0' ? '1' : '0');

        var valid = _sut.TryValidate(tampered, out var orderId, out var paymentId);

        valid.Should().BeFalse();
        orderId.Should().Be(Guid.Empty);
        paymentId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryValidate_WithTokenSignedByDifferentSecret_ReturnsFalse()
    {
        var token = Create("secret-a").Issue(Guid.NewGuid(), Guid.NewGuid());

        var valid = Create("secret-b").TryValidate(token, out _, out _);

        valid.Should().BeFalse();
    }

    [Fact]
    public void TryValidate_WithBlankOrNullToken_ReturnsFalse()
    {
        _sut.TryValidate(null, out _, out _).Should().BeFalse();
        _sut.TryValidate("   ", out _, out _).Should().BeFalse();
    }
}