namespace PaymentGateway.Core.Enums;

public enum PaymentStatus
{
    Pending,
    Processing,
    Successful,
    Failed,
    Refunded,
    PartiallyRefunded,
    Abandoned
}

public enum ProviderType
{
    Paystack,
    Interswitch,
    CyberSource
}

public enum Currency
{
    NGN,
    GBP,
    USD,
    EUR
}

public enum WebhookStatus
{
    Pending,
    Delivered,
    Failed,
    DeadLetter
}

