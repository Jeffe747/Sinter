using Microsoft.Extensions.Caching.Memory;

namespace LinuxAgent.Services;

public interface IIPLockoutService
{
    bool IsLockedOut(string ipAddress);
    void RegisterFailedAttempt(string ipAddress);
}

public class IPLockoutService : IIPLockoutService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<IPLockoutService> _logger;

    // Configuration
    private const int MaxFailedAttempts = 5;
    private readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(1);
    private readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    public IPLockoutService(IMemoryCache cache, ILogger<IPLockoutService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public bool IsLockedOut(string ipAddress)
    {
        return _cache.TryGetValue($"Lockout_{ipAddress}", out _);
    }

    public void RegisterFailedAttempt(string ipAddress)
    {
        var attemptsKey = $"Attempts_{ipAddress}";
        
        var attempts = _cache.GetOrCreate(attemptsKey, entry =>
        {
            entry.SlidingExpiration = AttemptWindow;
            return 0;
        });

        attempts++;
        _cache.Set(attemptsKey, attempts, AttemptWindow);

        if (attempts >= MaxFailedAttempts)
        {
            _logger.LogWarning("IP {IP} locked out due to too many failed attempts.", ipAddress);
            _cache.Set($"Lockout_{ipAddress}", true, LockoutDuration);
            _cache.Remove(attemptsKey); // Reset attempts count after lockout
        }
    }
}
