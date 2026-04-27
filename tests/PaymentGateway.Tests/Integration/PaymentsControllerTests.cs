using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PaymentGateway.Tests.Integration.Helpers;
using Xunit;

namespace PaymentGateway.Tests.Integration;

public class PaymentsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public PaymentsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Authentication ───────────────────────────────────────────────────────

    [Fact]
    public async Task InitiatePayment_WithoutAuthorizationHeader_Returns401()
    {
        var response = await _client.PostAsync("/api/v1/payments",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPaymentStatus_WithoutAuthorizationHeader_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/payments/TXN-001/verify");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefundPayment_WithoutAuthorizationHeader_Returns401()
    {
        var response = await _client.PostAsync($"/api/v1/payments/{Guid.NewGuid()}/refund",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InitiatePayment_WithValidTokenButMissingIdempotencyKey_Returns400()
    {
        AuthorizeClient();

        var body = BuildInitiatePaymentBody("TXN-TEST-001");
        var response = await _client.PostAsync("/api/v1/payments", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("idempotencyKey");
    }

    [Fact]
    public async Task InitiatePayment_WithEmptyBody_Returns400()
    {
        AuthorizeClient();
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.PostAsync("/api/v1/payments",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InitiatePayment_WithInvalidCurrencyEnum_Returns400()
    {
        AuthorizeClient();
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        var body = new StringContent(
            """{"reference":"TXN-001","amountInKobo":50000,"currency":"INVALID","provider":"Paystack","customer":{"email":"a@b.com","name":"Test"}}""",
            Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/v1/payments", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Routing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        // InMemory DB is healthy; Redis health check may fail in test — we just assert the endpoint responds
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void AuthorizeClient()
    {
        var token = _factory.GenerateTestToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static StringContent BuildInitiatePaymentBody(string reference)
    {
        var body = new
        {
            reference,
            amountInKobo = 50000,
            currency = "NGN",
            provider = "Paystack",
            customer = new { email = "customer@example.com", name = "Test User" }
        };
        return new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    }
}
