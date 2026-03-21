namespace Phylet.Data;

public sealed record RuntimeOptions(
    string Manufacturer,
    string ModelName,
    int DefaultSubscriptionTimeoutSeconds);
