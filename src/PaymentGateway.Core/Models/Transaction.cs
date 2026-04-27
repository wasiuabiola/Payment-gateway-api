using PaymentGateway.Core.Enums;

namespace PaymentGateway.Core.Models;

public class Transaction
{
    public Guid Id { get; private set; }
    public string Reference { get; private set; } = default!;
    public long AmountInKobo { get; private set; }
    public Currency Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public ProviderType Provider { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? AuthorizationUrl { get; private set; }
    public string? FailureReason { get; private set; }
    public CustomerInfo Customer { get; private set; } = default!;
    public Dictionary<string, string> Metadata { get; private set; } = new();
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public List<TransactionEvent> Events { get; private set; } = new();

    private Transaction() { }

    public static Transaction Create(
        string reference,
        long amountInKobo,
        Currency currency,
        ProviderType provider,
        CustomerInfo customer,
        Dictionary<string, string>? metadata = null)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            Reference = reference,
            AmountInKobo = amountInKobo,
            Currency = currency,
            Provider = provider,
            Customer = customer,
            Status = PaymentStatus.Pending,
            Metadata = metadata ?? new(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void BeginProcessing(string providerReference, string authorizationUrl)
    {
        Status = PaymentStatus.Processing;
        ProviderReference = providerReference;
        AuthorizationUrl = authorizationUrl;
        UpdatedAt = DateTime.UtcNow;
        RecordEvent(nameof(BeginProcessing), $"Provider reference: {providerReference}");
    }

    public void Complete(string providerReference)
    {
        Status = PaymentStatus.Successful;
        ProviderReference = providerReference;
        UpdatedAt = DateTime.UtcNow;
        RecordEvent(nameof(Complete), "Payment confirmed by provider");
    }

    public void Fail(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        UpdatedAt = DateTime.UtcNow;
        RecordEvent(nameof(Fail), reason);
    }

    public void Refund()
    {
        Status = PaymentStatus.Refunded;
        UpdatedAt = DateTime.UtcNow;
        RecordEvent(nameof(Refund), "Full refund processed");
    }

    private void RecordEvent(string eventType, string description)
    {
        Events.Add(new TransactionEvent
        {
            Id = Guid.NewGuid(),
            TransactionId = Id,
            EventType = eventType,
            Description = description,
            OccurredAt = DateTime.UtcNow
        });
    }
}

public class CustomerInfo
{
    public string Email { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
}

public class TransactionEvent
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public string EventType { get; set; } = default!;
    public string Description { get; set; } = default!;
    public DateTime OccurredAt { get; set; }
}

public class WebhookDelivery
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public string Payload { get; set; } = default!;
    public WebhookStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
