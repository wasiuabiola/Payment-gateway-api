using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Interfaces;
using StackExchange.Redis;

namespace PaymentGateway.Infrastructure.Idempotency;

public class RedisIdempotencyService : IIdempotencyService
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisIdempotencyService> _logger;
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);
    private const string KeyPrefix = "idempotency:";

    public RedisIdempotencyService(IConnectionMultiplexer redis, ILogger<RedisIdempotencyService> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<IdempotencyResult?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _db.StringGetAsync($"{KeyPrefix}{key}");
            if (!value.HasValue) return null;

            return JsonSerializer.Deserialize<IdempotencyResult>(value!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency GET failed for key {Key}", key);
            return null;
        }
    }

    public async Task StoreAsync(string key, object response, CancellationToken ct = default)
    {
        try
        {
            var result = new IdempotencyResult(
                key,
                JsonSerializer.Serialize(response),
                DateTime.UtcNow);

            var json = JsonSerializer.Serialize(result);
            await _db.StringSetAsync($"{KeyPrefix}{key}", json, IdempotencyWindow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency STORE failed for key {Key}", key);
        }
    }
}
