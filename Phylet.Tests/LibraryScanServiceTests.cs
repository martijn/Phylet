using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Phylet.Data;
using Phylet.Data.Configuration;
using Phylet.Data.Library;
using Phylet.Services;
using Xunit;

namespace Phylet.Tests;

public sealed class LibraryScanServiceTests
{
    [Fact]
    public async Task StartAsync_RunsInitialScanAndPersistsLibrary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mediaRoot = await TempMediaDirectory.CreateAsync();
        await using var database = await TempSqliteDatabase.CreateAsync();

        var trackPath = mediaRoot.CreateFile("Album/track.mp3", [1, 2, 3, 4]);
        var metadataReader = new StubAudioMetadataReader();
        metadataReader.Set(trackPath, new AudioMetadata("Track", "Artist", "Artist", "Album", 1, 1, 1000));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAudioMetadataReader>(metadataReader);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment
        {
            ApplicationName = "Phylet",
            EnvironmentName = Environments.Production,
            ContentRootPath = mediaRoot.RootPath,
            ContentRootFileProvider = new NullFileProvider()
        });
        services.AddSingleton(Options.Create(new StorageOptions { MediaPath = mediaRoot.RootPath }));
        services.AddSingleton<MediaPathResolver>();
        services.AddSingleton(new RuntimeOptions("Phylet", "Phylet Music Server", 1800));
        services.AddDbContextFactory<PhyletDbContext>((sp, builder) =>
            builder.UseSqlite($"Data Source={database.DatabasePath}"));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<PhyletDbContext>>().CreateDbContext());
        services.AddScoped<LibraryScanner>();
        services.AddSingleton<LibraryScanService>();

        await using var serviceProvider = services.BuildServiceProvider();
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PhyletDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        var service = serviceProvider.GetRequiredService<LibraryScanService>();
        await service.StartAsync(cancellationToken);
        await WaitForAsync(
            () => service.Current is { IsInProgress: false, LastCompletedUtc: not null, LastError: null },
            cancellationToken);
        await service.StopAsync(cancellationToken);

        var status = service.Current;
        Assert.False(status.IsInProgress);
        Assert.False(status.IsQueued);
        Assert.NotNull(status.StartedUtc);
        Assert.NotNull(status.LastCompletedUtc);
        Assert.Null(status.LastError);

        await using var verificationScope = serviceProvider.CreateAsyncScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<PhyletDbContext>();
        Assert.Equal(1, await verificationDbContext.Tracks.CountAsync(cancellationToken));
        Assert.Equal(1, await verificationDbContext.LibraryScanStates.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task StartAsync_StoresFailureOnStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await TempSqliteDatabase.CreateAsync();
        var missingMediaRoot = Path.Combine(Path.GetTempPath(), $"phylet-missing-media-{Guid.NewGuid():N}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAudioMetadataReader>(new StubAudioMetadataReader());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment
        {
            ApplicationName = "Phylet",
            EnvironmentName = Environments.Production,
            ContentRootPath = missingMediaRoot,
            ContentRootFileProvider = new NullFileProvider()
        });
        services.AddSingleton(Options.Create(new StorageOptions { MediaPath = missingMediaRoot }));
        services.AddSingleton<MediaPathResolver>();
        services.AddSingleton(new RuntimeOptions("Phylet", "Phylet Music Server", 1800));
        services.AddDbContextFactory<PhyletDbContext>((sp, builder) =>
            builder.UseSqlite($"Data Source={database.DatabasePath}"));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<PhyletDbContext>>().CreateDbContext());
        services.AddScoped<LibraryScanner>();
        services.AddSingleton<LibraryScanService>();

        await using var serviceProvider = services.BuildServiceProvider();
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PhyletDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        var service = serviceProvider.GetRequiredService<LibraryScanService>();
        await service.StartAsync(cancellationToken);
        await WaitForAsync(
            () => service.Current is { IsInProgress: false, LastError: not null },
            cancellationToken);
        await service.StopAsync(cancellationToken);

        var status = service.Current;
        Assert.False(status.IsInProgress);
        Assert.NotNull(status.StartedUtc);
        Assert.Null(status.LastCompletedUtc);
        Assert.Contains("Configured media path does not exist", status.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestScan_DuringActiveScan_QueuesFollowUpScan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mediaRoot = await TempMediaDirectory.CreateAsync();
        await using var database = await TempSqliteDatabase.CreateAsync();

        var firstTrackPath = mediaRoot.CreateFile("Album/track-1.mp3", [1, 2, 3, 4]);

        var metadataReader = new BlockingAudioMetadataReader();
        metadataReader.Set(firstTrackPath, new AudioMetadata("Track 1", "Artist", "Artist", "Album", 1, 1, 1000));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAudioMetadataReader>(metadataReader);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment
        {
            ApplicationName = "Phylet",
            EnvironmentName = Environments.Production,
            ContentRootPath = mediaRoot.RootPath,
            ContentRootFileProvider = new NullFileProvider()
        });
        services.AddSingleton(Options.Create(new StorageOptions { MediaPath = mediaRoot.RootPath }));
        services.AddSingleton<MediaPathResolver>();
        services.AddSingleton(new RuntimeOptions("Phylet", "Phylet Music Server", 1800));
        services.AddDbContextFactory<PhyletDbContext>((sp, builder) =>
            builder.UseSqlite($"Data Source={database.DatabasePath}"));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<PhyletDbContext>>().CreateDbContext());
        services.AddScoped<LibraryScanner>();
        services.AddSingleton<LibraryScanService>();

        await using var serviceProvider = services.BuildServiceProvider();
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PhyletDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        var service = serviceProvider.GetRequiredService<LibraryScanService>();
        await service.StartAsync(cancellationToken);
        await WaitForAsync(() => service.Current.IsInProgress, cancellationToken);

        service.RequestScan();
        Assert.True(service.Current.IsQueued);

        var secondTrackPath = mediaRoot.CreateFile("Album/track-2.mp3", [5, 6, 7, 8]);
        metadataReader.Set(secondTrackPath, new AudioMetadata("Track 2", "Artist", "Artist", "Album", 2, 1, 1000));
        metadataReader.ReleaseCurrentScan();

        await WaitForAsync(
            () => metadataReader.ReadCount >= 2 && service.Current is { IsInProgress: false, IsQueued: false, LastCompletedUtc: not null },
            cancellationToken);
        await service.StopAsync(cancellationToken);

        await using var verificationScope = serviceProvider.CreateAsyncScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<PhyletDbContext>();
        Assert.Equal(2, await verificationDbContext.Tracks.CountAsync(cancellationToken));
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for expected scan state.");
    }

    private sealed class StubAudioMetadataReader : IAudioMetadataReader
    {
        private readonly Dictionary<string, AudioMetadata> _entries = new(StringComparer.Ordinal);

        public AudioMetadata Read(string filePath) => _entries[filePath];

        public void Set(string filePath, AudioMetadata metadata) => _entries[filePath] = metadata;
    }

    private sealed class BlockingAudioMetadataReader : IAudioMetadataReader
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, AudioMetadata> _entries = new(StringComparer.Ordinal);
        private int _readCount;

        public int ReadCount => _readCount;

        public AudioMetadata Read(string filePath)
        {
            Interlocked.Increment(ref _readCount);
            _release.Task.GetAwaiter().GetResult();
            return _entries[filePath];
        }

        public void Set(string filePath, AudioMetadata metadata) => _entries[filePath] = metadata;

        public void ReleaseCurrentScan() => _release.TrySetResult();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Phylet";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TempMediaDirectory : IAsyncDisposable
    {
        private TempMediaDirectory(string rootPath) => RootPath = rootPath;

        public string RootPath { get; }

        public static Task<TempMediaDirectory> CreateAsync()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), $"phylet-coordinator-media-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return Task.FromResult(new TempMediaDirectory(rootPath));
        }

        public string CreateFile(string relativePath, byte[] contents, bool overwrite = false)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!overwrite && File.Exists(path))
            {
                throw new InvalidOperationException($"File already exists: {path}");
            }

            File.WriteAllBytes(path, contents);
            return path;
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TempSqliteDatabase : IAsyncDisposable
    {
        private TempSqliteDatabase(string databasePath) => DatabasePath = databasePath;

        public string DatabasePath { get; }

        public static Task<TempSqliteDatabase> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"phylet-coordinator-{Guid.NewGuid():N}.db");
            return Task.FromResult(new TempSqliteDatabase(databasePath));
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(DatabasePath))
            {
                File.Delete(DatabasePath);
            }

            return ValueTask.CompletedTask;
        }
    }
}
