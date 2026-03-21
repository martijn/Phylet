using Microsoft.Extensions.DependencyInjection;
using Phylet.Data.Configuration;
using Phylet.Data.Library;

namespace Phylet.Data;

public static class HostInitializationExtensions
{
    public static async Task InitializePhyletAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var configurationInitializer = scope.ServiceProvider.GetRequiredService<DeviceConfigurationInitializer>();
        await configurationInitializer.InitializeAsync(cancellationToken);
    }
}
