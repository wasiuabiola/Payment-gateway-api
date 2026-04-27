namespace PaymentGateway.API.DTOs.Responses;

public record InitiatePaymentResponse(
    string TransactionId,
    string Status,
    string AuthorizationUrl,
    string Reference,
    string Provider,
    DateTime CreatedAt);

public record VerifyPaymentResponse(
    string Reference,
    string Status,
    string? ProviderReference);

public record RefundResponse(
    string RefundReference,
    string Status,
    DateTime ProcessedAt);

public record ErrorResponse(
    string Code,
    string Message,
    string? Details = null);
