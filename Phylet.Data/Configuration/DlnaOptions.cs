namespace Phylet.Data.Configuration;

public sealed class DlnaOptions
{
    public string Manufacturer { get; set; } = "Phylet";
    public string ModelName { get; set; } = "Phylet Music Server";
    public int DefaultSubscriptionTimeoutSeconds { get; set; } = 1800;
}
