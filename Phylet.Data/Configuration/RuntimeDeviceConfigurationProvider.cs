namespace Phylet.Data.Configuration;

public sealed class RuntimeDeviceConfigurationProvider : IDeviceConfigurationProvider
{
    private RuntimeDeviceConfiguration? _current;

    public RuntimeDeviceConfiguration Current =>
        _current ?? throw new InvalidOperationException("Device configuration has not been initialized yet.");

    public void Set(RuntimeDeviceConfiguration configuration)
    {
        _current = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }
}
