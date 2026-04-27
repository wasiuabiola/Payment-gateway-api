using System.Net;
using System.Text.Json;
using PaymentGateway.API.DTOs.Responses;
using PaymentGateway.Core.Exceptions;

namespace PaymentGateway.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);

        var (statusCode, response) = exception switch
        {
            TransactionNotFoundException ex => (HttpStatusCode.NotFound, new ErrorResponse(ex.ErrorCode, ex.Message)),
            DuplicateTransactionException ex => (HttpStatusCode.Conflict, new ErrorResponse(ex.ErrorCode, ex.Message)),
            InvalidWebhookSignatureException ex => (HttpStatusCode.BadRequest, new ErrorResponse(ex.ErrorCode, ex.Message)),
            InvalidTransactionStateException ex => (HttpStatusCode.BadRequest, new ErrorResponse(ex.ErrorCode, ex.Message)),
            ProviderException ex => (HttpStatusCode.BadGateway, new ErrorResponse(ex.ErrorCode, ex.Message)),
            PaymentGatewayException ex => (HttpStatusCode.BadRequest, new ErrorResponse(ex.ErrorCode, ex.Message)),
            _ => (HttpStatusCode.InternalServerError, new ErrorResponse("INTERNAL_ERROR", "An unexpected error occurred"))
        };

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, SerializerOptions));
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
