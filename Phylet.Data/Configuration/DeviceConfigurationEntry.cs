using System.ComponentModel.DataAnnotations;

namespace Phylet.Data.Configuration;

public sealed class DeviceConfigurationEntry
{
    [Key]
    [MaxLength(128)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string? Value { get; set; }
}
