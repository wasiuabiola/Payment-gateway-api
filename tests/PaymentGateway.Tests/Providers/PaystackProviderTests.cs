using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Infrastructure.Providers;
using Xunit;

namespace PaymentGateway.Tests.Providers;

public class PaystackProviderTests
{
    private const string TestSecretKey = "sk_test_abc123secret";

    // ── VerifyWebhookSignature ───────────────────────────────────────────────

    [Fact]
    public void VerifyWebhookSignature_WithMatchingHmacSha512_ReturnsTrue()
    {
        var provider = BuildProvider();
        var payload = "{\"event\":\"charge.success\",\"data\":{\"reference\":\"TXN-001\"}}";
        var signature = ComputeHmacSha512(payload, TestSecretKey);

        provider.VerifyWebhookSignature(payload, signature).Should().BeTrue();
    }

    [Fact]
    public void VerifyWebhookSignature_WithTamperedPayload_ReturnsFalse()
    {
        var provider = BuildProvider();
        var originalPayload = "{\"event\":\"charge.success\",\"data\":{\"reference\":\"TXN-001\"}}";
        var signature = ComputeHmacSha512(originalPayload, TestSecretKey);
        var tamperedPayload = originalPayload.Replace("TXN-001", "TXN-999");

        provider.VerifyWebhookSignature(tamperedPayload, signature).Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_WithWrongKey_ReturnsFalse()
    {
        var provider = BuildProvider();
        var payload = "{\"event\":\"charge.success\"}";
        var signatureFromWrongKey = ComputeHmacSha512(payload, "wrong-secret-key");

        provider.VerifyWebhookSignature(payload, signatureFromWrongKey).Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_IsCaseInsensitiveForHexDigits()
    {
        var provider = BuildProvider();
        var payload = "test-payload";
        var signatureLower = ComputeHmacSha512(payload, TestSecretKey);
        var signatureUpper = signatureLower.ToUpperInvariant();

        provider.VerifyWebhookSignature(payload, signatureUpper).Should().BeTrue();
    }

    // ── QueryStatusAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task QueryStatusAsync_WhenPaystackReturnsSuccess_MapsToSuccessful()
    {
        var responseJson = """
            {"status":true,"message":"Verification successful","data":{"status":"success","reference":"TXN-001","amount":50000,"currency":"NGN"}}
            """;
        var provider = BuildProvider(responseJson, HttpStatusCode.OK);

        var result = await provider.QueryStatusAsync("TXN-001");

        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(PaymentStatus.Successful);
        result.ProviderReference.Should().Be("TXN-001");
    }

    [Fact]
    public async Task QueryStatusAsync_WhenPaystackReturnsFailed_MapsToFailed()
    {
        var responseJson = """
            {"status":true,"message":"Verification successful","data":{"status":"failed","reference":"TXN-001","amount":50000,"currency":"NGN"}}
            """;
        var provider = BuildProvider(responseJson, HttpStatusCode.OK);

        var result = await provider.QueryStatusAsync("TXN-001");

        result.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task QueryStatusAsync_WhenPaystackReturnsAbandoned_MapsToAbandoned()
    {
        var responseJson = """
            {"status":true,"message":"Verification successful","data":{"status":"abandoned","reference":"TXN-001","amount":50000,"currency":"NGN"}}
            """;
        var provider = BuildProvider(responseJson, HttpStatusCode.OK);

        var result = await provider.QueryStatusAsync("TXN-001");

        result.Status.Should().Be(PaymentStatus.Abandoned);
    }

    [Fact]
    public async Task QueryStatusAsync_WhenPaystackReturnsUnknownStatus_MapsToPending()
    {
        var responseJson = """
            {"status":true,"message":"Verification successful","data":{"status":"ongoing","reference":"TXN-001","amount":50000,"currency":"NGN"}}
            """;
        var provider = BuildProvider(responseJson, HttpStatusCode.OK);

        var result = await provider.QueryStatusAsync("TXN-001");

        result.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task QueryStatusAsync_WhenHttpRequestFails_ReturnsFailedResult()
    {
        var provider = BuildProvider("{\"message\":\"Not found\"}", HttpStatusCode.NotFound);

        var result = await provider.QueryStatusAsync("UNKNOWN-REF");

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Should().Contain("NotFound");
    }

    // ── InitiateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task InitiateAsync_WhenPaystackReturnsSuccess_ReturnsAuthorizationUrl()
    {
        var responseJson = """
            {"status":true,"message":"Authorization URL created","data":{"authorization_url":"https://checkout.paystack.com/abc123","access_code":"abc123","reference":"TXN-001"}}
            """;
        var provider = BuildProvider(responseJson, HttpStatusCode.OK);
        var request = new InitiatePaymentRequest("TXN-001", 50000, Currency.NGN, ProviderType.Paystack,
            "customer@example.com", "Test User");

        var result = await provider.InitiateAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.AuthorizationUrl.Should().Be("https://checkout.paystack.com/abc123");
        result.ProviderReference.Should().Be("TXN-001");
    }

    [Fact]
    public async Task InitiateAsync_WhenPaystackReturnsHttpError_ReturnsFailure()
    {
        var provider = BuildProvider("{\"message\":\"Invalid key\"}", HttpStatusCode.Unauthorized);
        var request = new InitiatePaymentRequest("TXN-001", 50000, Currency.NGN, ProviderType.Paystack,
            "customer@example.com", "Test User");

        var result = await provider.InitiateAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task InitiateAsync_WhenPaystackStatusFalse_ReturnsFailureWithMessage()
    {
        var responseJson = """
            {"status":false,"message":"Duplicate transaction reference"}
            """;
        var provider = BuildProvider(responseJson, HttpStatusCode.OK);
        var request = new InitiatePaymentRequest("TXN-001", 50000, Currency.NGN, ProviderType.Paystack,
            "customer@example.com", "Test User");

        var result = await provider.InitiateAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Duplicate transaction reference");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PaystackProvider BuildProvider(
        string responseJson = "{}",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(responseJson, statusCode);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new PaystackOptions { SecretKey = TestSecretKey, BaseUrl = "https://api.paystack.co" });
        var logger = new Mock<ILogger<PaystackProvider>>().Object;
        return new PaystackProvider(httpClient, options, logger);
    }

    private static string ComputeHmacSha512(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA512(keyBytes);
        return Convert.ToHexString(hmac.ComputeHash(payloadBytes)).ToLowerInvariant();
    }

    private class FakeHttpMessageHandler(string responseBody, HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }
}
