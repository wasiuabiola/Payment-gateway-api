using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PaymentGateway.Core.Enums;
using PaymentGateway.Infrastructure.Providers;
using Xunit;

namespace PaymentGateway.Tests.Providers;

public class InterswitchProviderTests
{
    private const string TestClientSecret = "interswitch-test-secret";
    private const string TestClientId = "DSHG-NSE-123";

    // ── VerifyWebhookSignature ───────────────────────────────────────────────

    [Fact]
    public void VerifyWebhookSignature_WithMatchingBase64HmacSha256_ReturnsTrue()
    {
        var provider = BuildProvider();
        var payload = "{\"event\":\"payment.successful\",\"data\":{\"reference\":\"TXN-001\"}}";
        var signature = ComputeHmacSha256Base64(payload, TestClientSecret);

        provider.VerifyWebhookSignature(payload, signature).Should().BeTrue();
    }

    [Fact]
    public void VerifyWebhookSignature_WithTamperedPayload_ReturnsFalse()
    {
        var provider = BuildProvider();
        var originalPayload = "{\"data\":{\"reference\":\"TXN-001\"}}";
        var signature = ComputeHmacSha256Base64(originalPayload, TestClientSecret);
        var tamperedPayload = originalPayload.Replace("TXN-001", "TXN-HACK");

        provider.VerifyWebhookSignature(tamperedPayload, signature).Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_WithWrongSecret_ReturnsFalse()
    {
        var provider = BuildProvider();
        var payload = "some-payload";
        var signatureFromWrongKey = ComputeHmacSha256Base64(payload, "wrong-secret");

        provider.VerifyWebhookSignature(payload, signatureFromWrongKey).Should().BeFalse();
    }

    // ── QueryStatusAsync response code mapping ───────────────────────────────

    [Fact]
    public async Task QueryStatusAsync_WhenResponseCode00_MapsToSuccessful()
    {
        var responseJson = """{"responseCode":"00","responseDescription":"Approved","amount":"50000"}""";
        var provider = BuildProvider(responseJson, HttpStatusCode.OK);

        var result = await provider.QueryStatusAsync("TXN-001");

        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(PaymentStatus.Successful);
        result.ProviderReference.Should().Be("TXN-001");
    }

    [Fact]
    public async Task QueryStatusAsync_WhenResponseCodeZ6_MapsToPending()
    {
        var responseJson = """{"responseCode":"Z6","responseDescription":"Pending","amount":"50000"}""";
        var provider = BuildProvider(responseJson, HttpStatusCode.OK);

        var result = await provider.QueryStatusAsync("TXN-001");

        result.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task QueryStatusAsync_WhenResponseCodeIsUnknown_MapsToFailed()
    {
        var responseJson = """{"responseCode":"99","responseDescription":"System error","amount":"0"}""";
        var provider = BuildProvider(responseJson, HttpStatusCode.OK);

        var result = await provider.QueryStatusAsync("TXN-001");

        result.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task QueryStatusAsync_WhenHttpRequestFails_ReturnsFailedResult()
    {
        var provider = BuildProvider("{}", HttpStatusCode.BadRequest);

        var result = await provider.QueryStatusAsync("UNKNOWN-REF");

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(PaymentStatus.Failed);
        result.Error.Should().Contain("BadRequest");
    }

    // ── RefundAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RefundAsync_AlwaysReturnsSuccessWithPrefixedReference()
    {
        var provider = BuildProvider();

        var result = await provider.RefundAsync("TXN-001", 50000);

        result.IsSuccess.Should().BeTrue();
        result.RefundReference.Should().Be("REF-TXN-001");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static InterswitchProvider BuildProvider(
        string responseJson = "{}",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(responseJson, statusCode);
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new InterswitchOptions
        {
            ClientId = TestClientId,
            ClientSecret = TestClientSecret,
            BaseUrl = "https://qa.interswitchng.com",
            TerminalId = "9999",
            CallbackUrl = "https://example.com/callback"
        });
        var logger = new Mock<ILogger<InterswitchProvider>>().Object;
        return new InterswitchProvider(httpClient, options, logger);
    }

    private static string ComputeHmacSha256Base64(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToBase64String(hmac.ComputeHash(payloadBytes));
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
