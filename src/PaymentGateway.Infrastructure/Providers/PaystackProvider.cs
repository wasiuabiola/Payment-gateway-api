using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Exceptions;
using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.Infrastructure.Providers;

public class PaystackOptions
{
    public string SecretKey { get; set; } = default!;
    public string BaseUrl { get; set; } = "https://api.paystack.co";
}

public class PaystackProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private readonly PaystackOptions _options;
    private readonly ILogger<PaystackProvider> _logger;

    public ProviderType ProviderType => ProviderType.Paystack;

    // Paystack status strings as returned by their API
    private static class TransactionStatus
    {
        public const string Success = "success";
        public const string Failed = "failed";
        public const string Abandoned = "abandoned";
    }

    private static class Endpoint
    {
        public const string InitiateTransaction = "/transaction/initialize";
        public const string VerifyTransaction = "/transaction/verify/";
        public const string CreateRefund = "/refund";
    }

    public PaystackProvider(
        HttpClient httpClient,
        IOptions<PaystackOptions> options,
        ILogger<PaystackProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.SecretKey}");
    }

    public async Task<InitiatePaymentResult> InitiateAsync(
        InitiatePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                email = request.CustomerEmail,
                amount = request.AmountInKobo,
                currency = request.Currency.ToString(),
                reference = request.Reference,
                metadata = new
                {
                    customer_name = request.CustomerName,
                    custom_fields = request.Metadata?.Select(kv => new { display_name = kv.Key, value = kv.Value }).ToArray()
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(Endpoint.InitiateTransaction, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Paystack initiation failed for {Reference}: {Body}", request.Reference, responseBody);
                return new InitiatePaymentResult(false, null, null, null, $"Paystack error: {response.StatusCode}");
            }

            var result = JsonSerializer.Deserialize<PaystackInitResponse>(responseBody, JsonOptions);

            if (result?.Status != true)
                return new InitiatePaymentResult(false, null, null, null, result?.Message ?? "Unknown error");

            return new InitiatePaymentResult(true, null, result.Data.Reference, result.Data.AuthorizationUrl, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paystack initiation threw exception for {Reference}", request.Reference);
            throw new ProviderException("Paystack", ex.Message);
        }
    }

    public async Task<PaymentStatusResult> QueryStatusAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{Endpoint.VerifyTransaction}{reference}", cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new PaymentStatusResult(false, PaymentStatus.Failed, null, $"Paystack error: {response.StatusCode}");

            var result = JsonSerializer.Deserialize<PaystackVerifyResponse>(responseBody, JsonOptions);

            if (result?.Status != true)
                return new PaymentStatusResult(false, PaymentStatus.Failed, null, result?.Message);

            var status = result.Data.Status switch
            {
                TransactionStatus.Success   => PaymentStatus.Successful,
                TransactionStatus.Failed    => PaymentStatus.Failed,
                TransactionStatus.Abandoned => PaymentStatus.Abandoned,
                _                           => PaymentStatus.Pending
            };

            return new PaymentStatusResult(true, status, result.Data.Reference, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paystack status query threw exception for {Reference}", reference);
            throw new ProviderException("Paystack", ex.Message);
        }
    }

    public async Task<RefundResult> RefundAsync(
        string providerReference,
        long amountInKobo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { transaction = providerReference, amount = amountInKobo };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(Endpoint.CreateRefund, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new RefundResult(false, null, $"Paystack refund error: {response.StatusCode}");

            var result = JsonSerializer.Deserialize<PaystackRefundResponse>(responseBody, JsonOptions);

            return result?.Status == true
                ? new RefundResult(true, result.Data.Id.ToString(), null)
                : new RefundResult(false, null, result?.Message ?? "Refund failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paystack refund threw exception for {Reference}", providerReference);
            throw new ProviderException("Paystack", ex.Message);
        }
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SecretKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA512(keyBytes);
        var computedHash = Convert.ToHexString(hmac.ComputeHash(payloadBytes)).ToLowerInvariant();

        return computedHash == signature.ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private record PaystackInitResponse(bool Status, string Message, PaystackInitData Data);
    private record PaystackInitData(string Reference, string AuthorizationUrl, string AccessCode);
    private record PaystackVerifyResponse(bool Status, string? Message, PaystackVerifyData Data);
    private record PaystackVerifyData(string Status, string Reference, long Amount, string Currency);
    private record PaystackRefundResponse(bool Status, string? Message, PaystackRefundData Data);
    private record PaystackRefundData(int Id, string Status);
}
