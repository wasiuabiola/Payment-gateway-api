using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Models;
using PaymentGateway.Infrastructure.Data;

namespace PaymentGateway.Infrastructure.Services;

public class WebhookService : IWebhookService
{
    private readonly PaymentDbContext _db;
    private readonly ILogger<WebhookService> _logger;

    // Exponential backoff delays in seconds: 30s, 5m, 30m, 2h
    private static readonly int[] BackoffSeconds = [30, 300, 1800, 7200];
    private const int MaxAttempts = 5;

    public WebhookService(PaymentDbContext db, ILogger<WebhookService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnqueueAsync(Guid transactionId, string payload, CancellationToken ct = default)
    {
        var delivery = new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Payload = payload,
            Status = WebhookStatus.Pending,
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _db.WebhookDeliveries.AddAsync(delivery, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Webhook delivery enqueued for transaction {TransactionId}", transactionId);
    }

    public async Task ProcessPendingAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var due = await _db.WebhookDeliveries
            .Where(w =>
                (w.Status == WebhookStatus.Pending || w.Status == WebhookStatus.Failed) &&
                w.AttemptCount < MaxAttempts &&
                (w.NextRetryAt == null || w.NextRetryAt <= now))
            .OrderBy(w => w.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (due.Count == 0)
            return;

        foreach (var delivery in due)
        {
            delivery.AttemptCount++;
            delivery.UpdatedAt = now;

            if (delivery.AttemptCount >= MaxAttempts)
            {
                delivery.Status = WebhookStatus.DeadLetter;
                _logger.LogWarning(
                    "Webhook {Id} moved to dead-letter after {Attempts} failed attempts",
                    delivery.Id, delivery.AttemptCount);
            }
            else
            {
                var delaySecs = BackoffSeconds[Math.Min(delivery.AttemptCount - 1, BackoffSeconds.Length - 1)];
                delivery.Status = WebhookStatus.Failed;
                delivery.NextRetryAt = now.AddSeconds(delaySecs);
                _logger.LogInformation(
                    "Webhook {Id} attempt {Attempt} failed — next retry at {RetryAt}",
                    delivery.Id, delivery.AttemptCount, delivery.NextRetryAt);
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Processed {Count} pending webhook deliveries", due.Count);
    }
}
