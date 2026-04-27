namespace PaymentGateway.Core.Exceptions;

public class PaymentGatewayException : Exception
{
    public string ErrorCode { get; }

    public PaymentGatewayException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}

public class TransactionNotFoundException : PaymentGatewayException
{
    public TransactionNotFoundException(string reference)
        : base($"Transaction with reference '{reference}' was not found.", "TRANSACTION_NOT_FOUND") { }
}

public class DuplicateTransactionException : PaymentGatewayException
{
    public DuplicateTransactionException(string reference)
        : base($"Transaction with reference '{reference}' already exists.", "DUPLICATE_TRANSACTION") { }
}

public class InvalidWebhookSignatureException : PaymentGatewayException
{
    public InvalidWebhookSignatureException()
        : base("Webhook signature verification failed.", "INVALID_WEBHOOK_SIGNATURE") { }
}

public class ProviderException : PaymentGatewayException
{
    public ProviderException(string provider, string message)
        : base($"Provider '{provider}' error: {message}", "PROVIDER_ERROR") { }
}

public class InvalidTransactionStateException : PaymentGatewayException
{
    public InvalidTransactionStateException(string currentState, string requiredState)
        : base($"Transaction is in '{currentState}' state. Required state: '{requiredState}'.", "INVALID_TRANSACTION_STATE") { }
}
