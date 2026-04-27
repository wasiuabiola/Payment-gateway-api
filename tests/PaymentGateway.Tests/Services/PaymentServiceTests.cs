using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Exceptions;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Models;
using PaymentGateway.Infrastructure.Services;
using Xunit;

namespace PaymentGateway.Tests.Services;

public class PaymentServiceTests
{
    private readonly Mock<IPaymentProvider> _paystackProviderMock;
    private readonly Mock<ITransactionRepository> _repositoryMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _paystackProviderMock = new Mock<IPaymentProvider>();
        _repositoryMock = new Mock<ITransactionRepository>();
        _cacheMock = new Mock<ICacheService>();
        var loggerMock = new Mock<ILogger<PaymentService>>();

        _paystackProviderMock.Setup(p => p.ProviderType).Returns(ProviderType.Paystack);

        _sut = new PaymentService(
            [_paystackProviderMock.Object],
            _repositoryMock.Object,
            _cacheMock.Object,
            loggerMock.Object);
    }

    // ── InitiateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task InitiateAsync_WithNewReference_SavesTransactionAndReturnsSuccess()
    {
        var request = BuildRequest("TXN-001");

        _repositoryMock
            .Setup(r => r.GetByReferenceAsync("TXN-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction?)null);

        _paystackProviderMock
            .Setup(p => p.InitiateAsync(It.IsAny<InitiatePaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiatePaymentResult(true, null, "paystack_ref_001", "https://checkout.paystack.com/abc", null));

        var result = await _sut.InitiateAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.TransactionId.Should().NotBeNullOrEmpty();
        result.ProviderReference.Should().Be("paystack_ref_001");
        result.AuthorizationUrl.Should().Be("https://checkout.paystack.com/abc");
        _repositoryMock.Verify(r => r.CreateAsync(
            It.Is<Transaction>(t => t.Reference == "TXN-001" && t.Status == PaymentStatus.Processing),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateAsync_WithDuplicateReference_ThrowsDuplicateTransactionException()
    {
        var request = BuildRequest("TXN-DUP");
        var existing = BuildTransaction("TXN-DUP");

        _repositoryMock
            .Setup(r => r.GetByReferenceAsync("TXN-DUP", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await _sut.Invoking(s => s.InitiateAsync(request))
            .Should().ThrowAsync<DuplicateTransactionException>();

        _paystackProviderMock.Verify(
            p => p.InitiateAsync(It.IsAny<InitiatePaymentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InitiateAsync_WhenProviderFails_ReturnsFailureWithoutSavingTransaction()
    {
        var request = BuildRequest("TXN-002");

        _repositoryMock
            .Setup(r => r.GetByReferenceAsync("TXN-002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction?)null);

        _paystackProviderMock
            .Setup(p => p.InitiateAsync(It.IsAny<InitiatePaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiatePaymentResult(false, null, null, null, "Provider unavailable"));

        var result = await _sut.InitiateAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Provider unavailable");
        _repositoryMock.Verify(r => r.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── GetPaymentStatusAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetPaymentStatusAsync_WithCachedResult_ReturnsCachedValueWithoutHittingRepository()
    {
        var cached = new PaymentStatusResult(true, PaymentStatus.Successful, "paystack_ref_001", null);

        _cacheMock
            .Setup(c => c.GetAsync<PaymentStatusResult>("payment:status:TXN-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var result = await _sut.GetPaymentStatusAsync("TXN-001");

        result.Status.Should().Be(PaymentStatus.Successful);
        _repositoryMock.Verify(r => r.GetByReferenceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _paystackProviderMock.Verify(p => p.QueryStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPaymentStatusAsync_WhenPaymentSuccessful_UpdatesTransactionAndCachesResult()
    {
        var transaction = BuildTransaction("TXN-001");
        transaction.BeginProcessing("paystack_ref_001", "https://checkout.paystack.com/abc");

        _cacheMock
            .Setup(c => c.GetAsync<PaymentStatusResult>("payment:status:TXN-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentStatusResult?)null);

        _repositoryMock
            .Setup(r => r.GetByReferenceAsync("TXN-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        _paystackProviderMock
            .Setup(p => p.QueryStatusAsync("TXN-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentStatusResult(true, PaymentStatus.Successful, "paystack_ref_001", null));

        var result = await _sut.GetPaymentStatusAsync("TXN-001");

        result.Status.Should().Be(PaymentStatus.Successful);
        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<Transaction>(t => t.Status == PaymentStatus.Successful),
            It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.SetAsync(
            "payment:status:TXN-001",
            It.IsAny<PaymentStatusResult>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPaymentStatusAsync_WithUnknownReference_ThrowsTransactionNotFoundException()
    {
        _cacheMock
            .Setup(c => c.GetAsync<PaymentStatusResult>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentStatusResult?)null);

        _repositoryMock
            .Setup(r => r.GetByReferenceAsync("MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction?)null);

        await _sut.Invoking(s => s.GetPaymentStatusAsync("MISSING"))
            .Should().ThrowAsync<TransactionNotFoundException>();
    }

    // ── RefundAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RefundAsync_WithSuccessfulTransaction_ProcessesRefundAndMarksAsRefunded()
    {
        var transaction = BuildTransaction("TXN-001");
        transaction.BeginProcessing("paystack_ref_001", "https://checkout.paystack.com/abc");
        transaction.Complete("paystack_ref_001");

        _repositoryMock
            .Setup(r => r.GetByIdAsync(transaction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        _paystackProviderMock
            .Setup(p => p.RefundAsync("paystack_ref_001", 50000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult(true, "REF-001", null));

        var result = await _sut.RefundAsync(transaction.Id, 50000, "Customer request");

        result.IsSuccess.Should().BeTrue();
        result.RefundReference.Should().Be("REF-001");
        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<Transaction>(t => t.Status == PaymentStatus.Refunded),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefundAsync_WithPendingTransaction_ThrowsInvalidTransactionStateException()
    {
        var transaction = BuildTransaction("TXN-001"); // Status is Pending

        _repositoryMock
            .Setup(r => r.GetByIdAsync(transaction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        await _sut.Invoking(s => s.RefundAsync(transaction.Id, 50000, "reason"))
            .Should().ThrowAsync<InvalidTransactionStateException>();

        _paystackProviderMock.Verify(
            p => p.RefundAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefundAsync_WithUnknownTransactionId_ThrowsTransactionNotFoundException()
    {
        var unknownId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.GetByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction?)null);

        await _sut.Invoking(s => s.RefundAsync(unknownId, 50000, "reason"))
            .Should().ThrowAsync<TransactionNotFoundException>();
    }

    // ── HandleWebhookAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleWebhookAsync_WithInvalidSignature_ThrowsInvalidWebhookSignatureException()
    {
        _paystackProviderMock
            .Setup(p => p.VerifyWebhookSignature(It.IsAny<string>(), "bad-sig"))
            .Returns(false);

        await _sut.Invoking(s => s.HandleWebhookAsync(ProviderType.Paystack, "{}", "bad-sig"))
            .Should().ThrowAsync<InvalidWebhookSignatureException>();
    }

    [Fact]
    public async Task HandleWebhookAsync_WithValidSignature_DoesNotThrow()
    {
        var payload = "{\"data\":{\"reference\":\"TXN-001\"}}";

        _paystackProviderMock
            .Setup(p => p.VerifyWebhookSignature(payload, "valid-sig"))
            .Returns(true);

        _paystackProviderMock
            .Setup(p => p.QueryStatusAsync("TXN-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentStatusResult(true, PaymentStatus.Successful, "paystack_ref_001", null));

        await _sut.Invoking(s => s.HandleWebhookAsync(ProviderType.Paystack, payload, "valid-sig"))
            .Should().NotThrowAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static InitiatePaymentRequest BuildRequest(string reference) =>
        new(reference, 50000, Currency.NGN, ProviderType.Paystack, "test@example.com", "Test User");

    private static Transaction BuildTransaction(string reference) =>
        Transaction.Create(reference, 50000, Currency.NGN, ProviderType.Paystack,
            new CustomerInfo { Email = "test@example.com", Name = "Test User" });
}
