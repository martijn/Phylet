namespace Phylet.Data.Configuration;

public interface IDeviceConfigurationProvider
{
    RuntimeDeviceConfiguration Current { get; }
}
