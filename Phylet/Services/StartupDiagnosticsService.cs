using Phylet.Data.Configuration;
using Phylet.Data.Library;

namespace Phylet.Services;

public sealed class StartupDiagnosticsService(
    ILogger<StartupDiagnosticsService> logger,
    IDeviceConfigurationProvider configurationProvider,
    ServerAddressResolver serverAddressResolver,
    MediaPathResolver mediaPathResolver,
    LibraryService library) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mediaRoot = mediaPathResolver.EnsureMediaDirectoryExists();
        var statistics = await library.GetStatisticsAsync(stoppingToken);
        var resolvedAddresses = await serverAddressResolver.ResolveAsync(stoppingToken);
        var configuration = configurationProvider.Current;

        logger.LogInformation(
            "Startup self-check: DeviceUuid={DeviceUuid}, FriendlyName={FriendlyName}, ListenAddresses={ListenAddresses}, AdvertisedBaseUrl={AdvertisedBaseUrl}, MediaRoot={MediaRoot}, Artists={ArtistCount}, Albums={AlbumCount}, Tracks={TrackCount}, Folders={FolderCount}, TotalAudioBytes={TotalAudioBytes}, LastScanUtc={LastScanUtc}",
            configuration.DeviceUuid,
            configuration.FriendlyName,
            string.Join(", ", resolvedAddresses.ListenAddresses.Select(address => address.ToString())),
            resolvedAddresses.AdvertisedBaseUri,
            mediaRoot,
            statistics.ArtistCount,
            statistics.AlbumCount,
            statistics.TrackCount,
            statistics.FolderCount,
            statistics.TotalAudioBytes,
            statistics.LastScanUtc);
    }
}
