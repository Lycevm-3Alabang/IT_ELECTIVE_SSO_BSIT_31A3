using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Gateway.Services;

public sealed class LoginRateLimitSettings
{
    /// <summary>Failed attempts allowed per email + IP pair inside the window.</summary>
    public int MaxAttempts { get; set; } = 5;

    public int WindowMinutes { get; set; } = 15;

    /// <summary>
    /// Backstop for one IP trying many different emails. Set well above MaxAttempts so
    /// a shared address (a school lab, an office NAT) isn't blocked by a few typos.
    /// </summary>
    public int MaxAttemptsPerIp { get; set; } = 20;
}

public readonly record struct RateLimitDecision(bool IsBlocked, TimeSpan RetryAfter);

public interface ILoginRateLimiter
{
    RateLimitDecision Check(string? ipAddress, string email);

    void RecordFailure(string? ipAddress, string email);
}

/// <summary>
/// Sliding-window limiter that counts failed logins. Kept in memory, so counters reset
/// on restart and aren't shared between server instances.
/// </summary>
public sealed class LoginRateLimiter : ILoginRateLimiter
{
    private const int MaxTrackedKeys = 10_000;
    private const int MaxKeyPartLength = 256;

    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _buckets = new();
    private readonly LoginRateLimitSettings _settings;
    private readonly TimeProvider _time;

    public LoginRateLimiter(IOptions<LoginRateLimitSettings> options, TimeProvider time)
    {
        _settings = options.Value;
        _time = time;
    }

    private TimeSpan Window => TimeSpan.FromMinutes(_settings.WindowMinutes);

    public RateLimitDecision Check(string? ipAddress, string email)
    {
        var now = _time.GetUtcNow();
        var perAccount = Evaluate(AccountKey(ipAddress, email), _settings.MaxAttempts, now);
        var perIp = Evaluate(IpKey(ipAddress), _settings.MaxAttemptsPerIp, now);

        if (!perAccount.IsBlocked && !perIp.IsBlocked)
        {
            return new RateLimitDecision(false, TimeSpan.Zero);
        }

        var retryAfter = TimeSpan.FromTicks(Math.Max(perAccount.RetryAfter.Ticks, perIp.RetryAfter.Ticks));
        return new RateLimitDecision(true, retryAfter);
    }

    public void RecordFailure(string? ipAddress, string email)
    {
        var now = _time.GetUtcNow();

        if (_buckets.Count > MaxTrackedKeys)
        {
            Sweep(now);
        }

        Add(AccountKey(ipAddress, email), now);
        Add(IpKey(ipAddress), now);
    }

    private RateLimitDecision Evaluate(string key, int max, DateTimeOffset now)
    {
        if (!_buckets.TryGetValue(key, out var bucket))
        {
            return new RateLimitDecision(false, TimeSpan.Zero);
        }

        lock (bucket)
        {
            Prune(bucket, now);
            if (bucket.Count < max)
            {
                return new RateLimitDecision(false, TimeSpan.Zero);
            }

            // The oldest failure leaves the window first; that's when the next attempt is allowed.
            var retryAfter = bucket[bucket.Count - max] + Window - now;
            return new RateLimitDecision(true, retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.Zero);
        }
    }

    private void Add(string key, DateTimeOffset now)
    {
        var bucket = _buckets.GetOrAdd(key, _ => new List<DateTimeOffset>());
        lock (bucket)
        {
            Prune(bucket, now);
            bucket.Add(now);
        }
    }

    private void Prune(List<DateTimeOffset> bucket, DateTimeOffset now)
    {
        var cutoff = now - Window;
        bucket.RemoveAll(t => t <= cutoff);
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (key, bucket) in _buckets)
        {
            lock (bucket)
            {
                Prune(bucket, now);
                if (bucket.Count == 0)
                {
                    _buckets.TryRemove(key, out _);
                }
            }
        }
    }

    private static string AccountKey(string? ip, string email) =>
        $"acct:{ip ?? "unknown"}|{Normalize(email)}";

    private static string IpKey(string? ip) => $"ip:{ip ?? "unknown"}";

    private static string Normalize(string email)
    {
        var trimmed = email.Trim().ToLowerInvariant();
        return trimmed.Length > MaxKeyPartLength ? trimmed[..MaxKeyPartLength] : trimmed;
    }
}
