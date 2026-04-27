using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Exceptions;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Models;

namespace PaymentGateway.Infrastructure.Services;

public class PaymentService : IPaymentService
{
    private readonly IEnumerable<IPaymentProvider> _providers;
    private readonly ITransactionRepository _repository;
    private readonly ICacheService _cache;
    private readonly ILogger<PaymentService> _logger;

    private const string StatusCacheKeyPrefix = "payment:status:";
    private static readonly TimeSpan StatusCacheDuration = TimeSpan.FromMinutes(5);

    public PaymentService(
        IEnumerable<IPaymentProvider> providers,
        ITransactionRepository repository,
        ICacheService cache,
        ILogger<PaymentService> logger)
    {
        _providers = providers;
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<InitiatePaymentResult> InitiateAsync(InitiatePaymentRequest request, CancellationToken ct = default)
    {
        var existing = await _repository.GetByReferenceAsync(request.Reference, ct);
        if (existing != null)
            throw new DuplicateTransactionException(request.Reference);

        var provider = GetProvider(request.Provider);
        var providerResult = await provider.InitiateAsync(request, ct);

        if (!providerResult.IsSuccess)
            return providerResult;

        var transaction = Transaction.Create(
            request.Reference,
            request.AmountInKobo,
            request.Currency,
            request.Provider,
            new CustomerInfo { Email = request.CustomerEmail, Name = request.CustomerName, Phone = request.CustomerPhone },
            request.Metadata);

        transaction.BeginProcessing(providerResult.ProviderReference!, providerResult.AuthorizationUrl!);
        await _repository.CreateAsync(transaction, ct);

        _logger.LogInformation("Payment initiated for reference {Reference} via {Provider}", request.Reference, request.Provider);

        return new InitiatePaymentResult(
            true,
            transaction.Id.ToString(),
            providerResult.ProviderReference,
            providerResult.AuthorizationUrl,
            null);
    }

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(string reference, CancellationToken ct = default)
    {
        var cacheKey = $"{StatusCacheKeyPrefix}{reference}";
        var cached = await _cache.GetAsync<PaymentStatusResult>(cacheKey, ct);
        if (cached != null)
            return cached;

        var transaction = await _repository.GetByReferenceAsync(reference, ct)
            ?? throw new TransactionNotFoundException(reference);

        var provider = GetProvider(transaction.Provider);
        var result = await provider.QueryStatusAsync(reference, ct);

        if (result.IsSuccess && result.Status == PaymentStatus.Successful)
        {
            transaction.Complete(result.ProviderReference!);
            await _repository.UpdateAsync(transaction, ct);
            await _cache.SetAsync(cacheKey, result, StatusCacheDuration, ct);
        }

        return result;
    }

    public async Task<RefundResult> RefundAsync(Guid transactionId, long amount, string reason, CancellationToken ct = default)
    {
        var transaction = await _repository.GetByIdAsync(transactionId, ct)
            ?? throw new TransactionNotFoundException(transactionId.ToString());

        if (transaction.Status != PaymentStatus.Successful)
            throw new InvalidTransactionStateException(transaction.Status.ToString(), nameof(PaymentStatus.Successful));

        var provider = GetProvider(transaction.Provider);
        var result = await provider.RefundAsync(transaction.ProviderReference!, amount, ct);

        if (result.IsSuccess)
        {
            transaction.Refund();
            await _repository.UpdateAsync(transaction, ct);
        }

        return result;
    }

    public async Task HandleWebhookAsync(ProviderType provider, string payload, string signature, CancellationToken ct = default)
    {
        var paymentProvider = GetProvider(provider);

        if (!paymentProvider.VerifyWebhookSignature(payload, signature))
            throw new InvalidWebhookSignatureException();

        var reference = ExtractReferenceFromPayload(payload);
        if (!string.IsNullOrEmpty(reference))
        {
            var result = await paymentProvider.QueryStatusAsync(reference, ct);
            _logger.LogInformation("Webhook processed for provider {Provider}, reference {Reference}, status: {Status}",
                provider, reference, result.Status);
        }
    }

    private IPaymentProvider GetProvider(ProviderType providerType)
        => _providers.FirstOrDefault(p => p.ProviderType == providerType)
           ?? throw new ProviderException(providerType.ToString(), "Provider not configured");

    private static string ExtractReferenceFromPayload(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("reference", out var reference))
                return reference.GetString() ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }
}
