using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Phylet.Data.Configuration;
using Phylet.Data.Library;

namespace Phylet.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPhyletData(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<StorageOptions>()
            .Bind(configuration.GetSection("Storage"));

        services.AddSingleton<RuntimeDeviceConfigurationProvider>();
        services.AddSingleton<IDeviceConfigurationProvider>(sp => sp.GetRequiredService<RuntimeDeviceConfigurationProvider>());
        services.AddSingleton<DatabasePathResolver>();
        services.AddSingleton<MediaPathResolver>();
        services.AddSingleton<IAudioMetadataReader, AtlAudioMetadataReader>();
        services.AddSingleton<LibraryService>();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DlnaOptions>>().Value;
            return new RuntimeOptions(options.Manufacturer, options.ModelName, options.DefaultSubscriptionTimeoutSeconds);
        });

        services.AddDbContextFactory<PhyletDbContext>((serviceProvider, builder) =>
        {
            var databasePath = serviceProvider.GetRequiredService<DatabasePathResolver>().ResolveDatabasePath();
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            builder.UseSqlite($"Data Source={databasePath}");
        });
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<PhyletDbContext>>().CreateDbContext());

        services.AddScoped<DeviceConfigurationInitializer>();
        services.AddScoped<LibraryScanner>();

        return services;
    }
}
