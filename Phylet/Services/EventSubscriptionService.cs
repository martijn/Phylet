using System.Collections.Concurrent;
using Phylet.Data.Configuration;

namespace Phylet.Services;

public sealed class EventSubscriptionService(IDeviceConfigurationProvider configurationProvider, TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, SubscriptionRecord> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

    public SubscriptionResult Subscribe(string serviceName, string? sidHeader, string? timeoutHeader)
    {
        var timeoutSeconds = ParseTimeoutSeconds(timeoutHeader);

        if (!string.IsNullOrWhiteSpace(sidHeader))
        {
            var sid = sidHeader.Trim();
            if (_subscriptions.TryGetValue(sid, out var existing))
            {
                _subscriptions[sid] = existing with { ExpiresAtUtc = timeProvider.GetUtcNow().AddSeconds(timeoutSeconds) };
                return new SubscriptionResult(true, sid, timeoutSeconds, true, serviceName);
            }

            return new SubscriptionResult(false, sid, timeoutSeconds, true, serviceName);
        }

        var newSid = $"uuid:{Guid.NewGuid()}";
        _subscriptions[newSid] = new SubscriptionRecord(serviceName, timeProvider.GetUtcNow().AddSeconds(timeoutSeconds));
        return new SubscriptionResult(true, newSid, timeoutSeconds, false, serviceName);
    }

    public bool Unsubscribe(string? sidHeader)
    {
        if (string.IsNullOrWhiteSpace(sidHeader))
        {
            return false;
        }

        return _subscriptions.TryRemove(sidHeader.Trim(), out _);
    }

    public int ActiveCount => _subscriptions.Count;

    private int ParseTimeoutSeconds(string? timeoutHeader)
    {
        if (string.IsNullOrWhiteSpace(timeoutHeader))
        {
            return configurationProvider.Current.DefaultSubscriptionTimeoutSeconds;
        }

        var token = timeoutHeader.Trim();
        if (token.Equals("Second-infinite", StringComparison.OrdinalIgnoreCase))
        {
            return 86400;
        }

        if (!token.StartsWith("Second-", StringComparison.OrdinalIgnoreCase))
        {
            return configurationProvider.Current.DefaultSubscriptionTimeoutSeconds;
        }

        var valuePart = token["Second-".Length..];
        return int.TryParse(valuePart, out var seconds)
            ? Math.Clamp(seconds, 60, 86400)
            : configurationProvider.Current.DefaultSubscriptionTimeoutSeconds;
    }

    private sealed record SubscriptionRecord(string ServiceName, DateTimeOffset ExpiresAtUtc);
}

public sealed record SubscriptionResult(bool Success, string Sid, int TimeoutSeconds, bool IsRenewal, string ServiceName);
