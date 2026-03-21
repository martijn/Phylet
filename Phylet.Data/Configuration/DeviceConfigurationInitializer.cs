using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Phylet.Data.Configuration;

public sealed class DeviceConfigurationInitializer(
    PhyletDbContext dbContext,
    RuntimeDeviceConfigurationProvider runtimeConfigurationProvider,
    ILogger<DeviceConfigurationInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        var entries = await dbContext.DeviceConfigurations
            .ToDictionaryAsync(entry => entry.Key, StringComparer.Ordinal, cancellationToken);

        var changed = false;
        var deviceUuid = GetValue(entries, DeviceConfigurationDefaults.DeviceUuidKey);
        var friendlyName = GetValue(entries, DeviceConfigurationDefaults.FriendlyNameKey);

        if (string.IsNullOrWhiteSpace(deviceUuid))
        {
            deviceUuid = $"uuid:{Guid.NewGuid()}";
            SetValue(entries, DeviceConfigurationDefaults.DeviceUuidKey, deviceUuid);
            changed = true;
            logger.LogInformation("Generated new DLNA device UUID {DeviceUuid}", deviceUuid);
        }

        if (!IsValidDeviceUuid(deviceUuid))
        {
            throw new InvalidOperationException($"Persisted device UUID '{deviceUuid}' is invalid. Expected format 'uuid:<guid>'.");
        }

        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            friendlyName = DeviceConfigurationDefaults.FriendlyName;
            SetValue(entries, DeviceConfigurationDefaults.FriendlyNameKey, friendlyName);
            changed = true;
            logger.LogInformation("Initialized default DLNA friendly name '{FriendlyName}'", friendlyName);
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        runtimeConfigurationProvider.Set(new RuntimeDeviceConfiguration(
            deviceUuid,
            friendlyName,
            dbContext.RuntimeOptions.Manufacturer,
            dbContext.RuntimeOptions.ModelName,
            dbContext.RuntimeOptions.DefaultSubscriptionTimeoutSeconds));
    }

    private void SetValue(IDictionary<string, DeviceConfigurationEntry> entries, string key, string value)
    {
        if (entries.TryGetValue(key, out var existing))
        {
            existing.Value = value;
            return;
        }

        var entry = new DeviceConfigurationEntry
        {
            Key = key,
            Value = value
        };

        dbContext.DeviceConfigurations.Add(entry);
        entries[key] = entry;
    }

    private static string? GetValue(IReadOnlyDictionary<string, DeviceConfigurationEntry> entries, string key) =>
        entries.TryGetValue(key, out var entry) ? entry.Value : null;

    private static bool IsValidDeviceUuid(string value)
    {
        if (!value.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Guid.TryParse(value["uuid:".Length..], out _);
    }
}
