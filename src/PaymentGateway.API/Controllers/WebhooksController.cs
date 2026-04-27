using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentGateway.API.DTOs.Responses;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.API.Controllers;

[ApiController]
[Route("api/v1/webhooks")]
[AllowAnonymous]
public class WebhooksController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(IPaymentService paymentService, ILogger<WebhooksController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>Receive Paystack webhook events</summary>
    [HttpPost("paystack")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> PaystackWebhook(
        [FromHeader(Name = "X-Paystack-Signature")] string signature,
        CancellationToken ct)
        => ProcessWebhook(ProviderType.Paystack, signature, ct);

    /// <summary>Receive Interswitch webhook events</summary>
    [HttpPost("interswitch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> InterswitchWebhook(
        [FromHeader(Name = "X-Interswitch-Signature")] string signature,
        CancellationToken ct)
        => ProcessWebhook(ProviderType.Interswitch, signature, ct);

    private async Task<IActionResult> ProcessWebhook(ProviderType provider, string signature, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return BadRequest(new ErrorResponse("MISSING_SIGNATURE", "Webhook signature is required"));

        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var payload = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        _logger.LogInformation("Webhook received from {Provider}", provider);

        await _paymentService.HandleWebhookAsync(provider, payload, signature, ct);
        return Ok();
    }
}
