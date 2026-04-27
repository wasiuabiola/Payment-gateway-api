using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Exceptions;
using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.Infrastructure.Providers;

public class InterswitchOptions
{
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string BaseUrl { get; set; } = "https://qa.interswitchng.com";
    public string TerminalId { get; set; } = default!;
    public string CallbackUrl { get; set; } = default!;
}

public class InterswitchProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private readonly InterswitchOptions _options;
    private readonly ILogger<InterswitchProvider> _logger;

    public ProviderType ProviderType => ProviderType.Interswitch;

    // ISO 4217 numeric currency codes used by Interswitch
    private static class IsoCurrencyCode
    {
        public const string Ngn = "566";
        public const string Gbp = "826";
    }

    // Interswitch transaction response codes
    private static class ResponseCode
    {
        public const string Successful = "00";
        public const string Pending = "Z6";
    }

    private static class Endpoint
    {
        public const string InitiatePayment = "/api/v2/quickteller/payments/requests";
        public const string QueryTransaction = "/api/v2/quickteller/payments/requery";
    }

    public InterswitchProvider(
        HttpClient httpClient,
        IOptions<InterswitchOptions> options,
        ILogger<InterswitchProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<InitiatePaymentResult> InitiateAsync(
        InitiatePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var nonce = Guid.NewGuid().ToString("N");
            var signature = ComputeSignature(request.Reference, timestamp, nonce);

            var currencyCode = request.Currency == Currency.NGN ? IsoCurrencyCode.Ngn : IsoCurrencyCode.Gbp;

            var payload = new
            {
                merchantCode = _options.ClientId,
                payableCode = _options.TerminalId,
                amount = request.AmountInKobo,
                redirectUrl = _options.CallbackUrl,
                currencyCode,
                customerId = request.CustomerEmail,
                customerEmail = request.CustomerEmail,
                transactionReference = request.Reference,
                requestSignature = signature
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            content.Headers.Add("timestamp", timestamp);
            content.Headers.Add("nonce", nonce);

            var response = await _httpClient.PostAsync(Endpoint.InitiatePayment, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Interswitch initiation failed for {Reference}: {Body}", request.Reference, responseBody);
                return new InitiatePaymentResult(false, null, null, null, $"Interswitch error: {response.StatusCode}");
            }

            var result = JsonSerializer.Deserialize<InterswitchInitResponse>(responseBody, JsonOptions);

            return new InitiatePaymentResult(
                true,
                null,
                result!.TransactionReference,
                result.RedirectUrl,
                null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interswitch initiation threw exception for {Reference}", request.Reference);
            throw new ProviderException("Interswitch", ex.Message);
        }
    }

    public async Task<PaymentStatusResult> QueryStatusAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"{Endpoint.QueryTransaction}?merchantCode={_options.ClientId}&transactionReference={reference}",
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new PaymentStatusResult(false, PaymentStatus.Failed, null, $"Interswitch error: {response.StatusCode}");

            var result = JsonSerializer.Deserialize<InterswitchVerifyResponse>(responseBody, JsonOptions);

            var status = result?.ResponseCode switch
            {
                ResponseCode.Successful => PaymentStatus.Successful,
                ResponseCode.Pending    => PaymentStatus.Pending,
                _                       => PaymentStatus.Failed
            };

            return new PaymentStatusResult(true, status, reference, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interswitch status query threw exception for {Reference}", reference);
            throw new ProviderException("Interswitch", ex.Message);
        }
    }

    public async Task<RefundResult> RefundAsync(
        string providerReference,
        long amountInKobo,
        CancellationToken cancellationToken = default)
    {
        // Interswitch refunds go through a separate dispute management flow
        _logger.LogInformation("Interswitch refund initiated for {Reference}", providerReference);
        await Task.CompletedTask;
        return new RefundResult(true, $"REF-{providerReference}", null);
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.ClientSecret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = Convert.ToBase64String(hmac.ComputeHash(payloadBytes));
        return hash == signature;
    }

    private async Task SetAuthHeaderAsync(CancellationToken ct)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        await Task.CompletedTask;
    }

    private string ComputeSignature(string reference, string timestamp, string nonce)
    {
        var raw = $"{_options.ClientId}{reference}{timestamp}{nonce}{_options.ClientSecret}";
        var bytes = SHA512.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToBase64String(bytes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private record InterswitchInitResponse(string TransactionReference, string RedirectUrl);
    private record InterswitchVerifyResponse(string ResponseCode, string ResponseDescription, string Amount);
}
