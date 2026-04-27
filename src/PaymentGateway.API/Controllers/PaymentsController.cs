using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentGateway.API.DTOs.Requests;
using PaymentGateway.API.DTOs.Responses;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using CoreInitiateRequest = PaymentGateway.Core.Interfaces.InitiatePaymentRequest;

namespace PaymentGateway.API.Controllers;

[ApiController]
[Route("api/v1/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentService paymentService,
        IIdempotencyService idempotencyService,
        ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    /// <summary>
    /// Initiate a payment transaction
    /// </summary>
    /// <remarks>
    /// Requires an Idempotency-Key header (UUID v4).
    /// Duplicate requests with the same key within 24 hours return the cached response.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(InitiatePaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InitiatePayment(
        [FromBody] CreatePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new ErrorResponse("MISSING_IDEMPOTENCY_KEY", "Idempotency-Key header is required"));

        var existing = await _idempotencyService.GetAsync(idempotencyKey, ct);
        if (existing != null)
        {
            _logger.LogInformation("Returning cached idempotent response for key {Key}", idempotencyKey);
            Response.Headers.Append("Idempotency-Replayed", "true");
            return Ok(existing.ResponseJson);
        }

        var serviceRequest = new CoreInitiateRequest(
            request.Reference,
            request.AmountInKobo,
            Enum.Parse<Currency>(request.Currency),
            Enum.Parse<ProviderType>(request.Provider),
            request.Customer.Email,
            request.Customer.Name,
            request.Customer.Phone,
            request.Metadata);

        var result = await _paymentService.InitiateAsync(serviceRequest, ct);

        if (!result.IsSuccess)
            return BadRequest(new ErrorResponse("PAYMENT_INITIATION_FAILED", result.Error!));

        var response = new InitiatePaymentResponse(
            result.TransactionId!,
            "Pending",
            result.AuthorizationUrl!,
            request.Reference,
            request.Provider,
            DateTime.UtcNow);

        await _idempotencyService.StoreAsync(idempotencyKey, response, ct);
        return Ok(response);
    }

    /// <summary>
    /// Verify a payment transaction by reference
    /// </summary>
    [HttpGet("{reference}/verify")]
    [ProducesResponseType(typeof(VerifyPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyPayment(string reference, CancellationToken ct)
    {
        var result = await _paymentService.GetPaymentStatusAsync(reference, ct);
        return Ok(new VerifyPaymentResponse(reference, result.Status.ToString(), result.ProviderReference));
    }

    /// <summary>
    /// Initiate a refund for a successful transaction
    /// </summary>
    [HttpPost("{transactionId}/refund")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefundPayment(
        Guid transactionId,
        [FromBody] RefundRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new ErrorResponse("MISSING_IDEMPOTENCY_KEY", "Idempotency-Key header is required"));

        var existing = await _idempotencyService.GetAsync(idempotencyKey, ct);
        if (existing != null)
        {
            Response.Headers.Append("Idempotency-Replayed", "true");
            return Ok(existing.ResponseJson);
        }

        var result = await _paymentService.RefundAsync(transactionId, request.Amount, request.Reason, ct);

        if (!result.IsSuccess)
            return BadRequest(new ErrorResponse("REFUND_FAILED", result.Error!));

        var response = new RefundResponse(result.RefundReference!, "Refunded", DateTime.UtcNow);
        await _idempotencyService.StoreAsync(idempotencyKey, response, ct);
        return Ok(response);
    }
}
