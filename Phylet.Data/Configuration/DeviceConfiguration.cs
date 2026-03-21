namespace Phylet.Data.Configuration;

public sealed record RuntimeDeviceConfiguration(
    string DeviceUuid,
    string FriendlyName,
    string Manufacturer,
    string ModelName,
    int DefaultSubscriptionTimeoutSeconds);
