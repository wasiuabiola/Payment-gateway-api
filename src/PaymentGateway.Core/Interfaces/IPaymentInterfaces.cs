using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Models;

namespace PaymentGateway.Core.Interfaces;

public interface IPaymentProvider
{
    ProviderType ProviderType { get; }

    Task<InitiatePaymentResult> InitiateAsync(
        InitiatePaymentRequest request,
        CancellationToken cancellationToken = default);

    // Renamed: VerifyAsync was ambiguous — are we verifying or querying?
    // QueryStatusAsync clearly communicates we are asking the provider for current status.
    Task<PaymentStatusResult> QueryStatusAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<RefundResult> RefundAsync(
        string providerReference,
        long amountInKobo,
        CancellationToken cancellationToken = default);

    bool VerifyWebhookSignature(string payload, string signature);
}

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Transaction?> GetByReferenceAsync(string reference, CancellationToken ct = default);
    Task<IEnumerable<Transaction>> GetByCustomerEmailAsync(string email, CancellationToken ct = default);
    Task CreateAsync(Transaction transaction, CancellationToken ct = default);
    Task UpdateAsync(Transaction transaction, CancellationToken ct = default);
}

public interface IIdempotencyService
{
    Task<IdempotencyResult?> GetAsync(string key, CancellationToken ct = default);
    Task StoreAsync(string key, object response, CancellationToken ct = default);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
}

public interface IWebhookService
{
    Task EnqueueAsync(Guid transactionId, string payload, CancellationToken ct = default);
    Task ProcessPendingAsync(CancellationToken ct = default);
}

public interface IPaymentService
{
    Task<InitiatePaymentResult> InitiateAsync(InitiatePaymentRequest request, CancellationToken ct = default);
    Task<PaymentStatusResult> GetPaymentStatusAsync(string reference, CancellationToken ct = default);
    Task<RefundResult> RefundAsync(Guid transactionId, long amount, string reason, CancellationToken ct = default);
    Task HandleWebhookAsync(ProviderType provider, string payload, string signature, CancellationToken ct = default);
}

// Result types
public record InitiatePaymentResult(
    bool IsSuccess,
    string? TransactionId,
    string? ProviderReference,
    string? AuthorizationUrl,
    string? Error);

public record PaymentStatusResult(
    bool IsSuccess,
    PaymentStatus Status,
    string? ProviderReference,
    string? Error);

public record RefundResult(
    bool IsSuccess,
    string? RefundReference,
    string? Error);

public record IdempotencyResult(
    string Key,
    string ResponseJson,
    DateTime CreatedAt);

// Request types
public record InitiatePaymentRequest(
    string Reference,
    long AmountInKobo,
    Currency Currency,
    ProviderType Provider,
    string CustomerEmail,
    string CustomerName,
    string? CustomerPhone = null,
    Dictionary<string, string>? Metadata = null);
