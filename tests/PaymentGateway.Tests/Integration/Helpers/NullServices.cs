using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.Tests.Integration.Helpers;

public class NullCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        => Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default)
        => Task.CompletedTask;
}

public class NullIdempotencyService : IIdempotencyService
{
    public Task<IdempotencyResult?> GetAsync(string key, CancellationToken ct = default)
        => Task.FromResult<IdempotencyResult?>(null);

    public Task StoreAsync(string key, object response, CancellationToken ct = default)
        => Task.CompletedTask;
}
