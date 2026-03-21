using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Phylet.Data.Configuration;
using Phylet.Data.Library;
using System.Text;
using Xunit;

namespace Phylet.Data.Tests;

public sealed class LibraryScannerTests
{
    [Fact]
    public async Task ScanAsync_ImportsTaggedAlbumsAndFilesViewFallback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var tempMedia = await TempMediaDirectory.CreateAsync();

        tempMedia.CreateDirectory("Tagged Album");
        var firstTrackPath = tempMedia.CreateFile("Tagged Album/01-first.mp3", [1, 2, 3]);
        var secondTrackPath = tempMedia.CreateFile("Tagged Album/02-second.mp3", [4, 5, 6]);
        var orphanTrackPath = tempMedia.CreateFile("root-orphan.mp3", [7, 8, 9]);
        tempMedia.CreateFile("Tagged Album/cover.jpg", [10, 11, 12]);

        var metadataReader = new StubAudioMetadataReader();
        metadataReader.Set(firstTrackPath, new AudioMetadata("First Song", "The Artist", "The Artist", "The Album", 1, 1, 1000));
        metadataReader.Set(secondTrackPath, new AudioMetadata("Second Song", "The Artist", "The Artist", "The Album", 2, 1, 2000));
        metadataReader.Set(orphanTrackPath, new AudioMetadata(null, null, null, null, null, null, null));

        var scanner = new LibraryScanner(
            fixture.DbContext,
            metadataReader,
            CreateMediaPathResolver(tempMedia.RootPath, Environments.Production),
            NullLogger<LibraryScanner>.Instance);

        await scanner.ScanAsync(cancellationToken);

        var artists = await fixture.DbContext.Artists.AsNoTracking().ToListAsync(cancellationToken);
        var albums = await fixture.DbContext.Albums.AsNoTracking().ToListAsync(cancellationToken);
        var tracks = await fixture.DbContext.Tracks.AsNoTracking().OrderBy(track => track.RelativePath).ToListAsync(cancellationToken);
        var folders = await fixture.DbContext.Folders.AsNoTracking().ToListAsync(cancellationToken);

        Assert.Single(artists);
        Assert.Single(albums);
        Assert.Equal(3, tracks.Count);
        Assert.Single(folders);
        Assert.Equal("The Artist", artists[0].Name);
        Assert.Equal("The Album", albums[0].Title);
        Assert.Equal("Tagged Album/cover.jpg", albums[0].CoverRelativePath);

        var taggedTracks = tracks.Where(track => track.AlbumId == albums[0].Id).OrderBy(track => track.TrackNumber).ToArray();
        Assert.Equal(["First Song", "Second Song"], taggedTracks.Select(track => track.Title));

        var orphanTrack = Assert.Single(tracks, track => track.AlbumId is null);
        Assert.Equal("root-orphan", orphanTrack.Title);
        Assert.Null(orphanTrack.FolderId);
    }

    [Fact]
    public async Task ScanAsync_ImportsSupportedFormatsBeyondMp3()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var tempMedia = await TempMediaDirectory.CreateAsync();

        tempMedia.CreateDirectory("Mixed Album");
        var flacPath = tempMedia.CreateFile("Mixed Album/01-track.flac", [1, 2]);
        var m4aPath = tempMedia.CreateFile("Mixed Album/02-track.m4a", [3, 4]);
        var oggPath = tempMedia.CreateFile("Mixed Album/03-track.ogg", [5, 6]);

        var metadataReader = new StubAudioMetadataReader();
        metadataReader.Set(flacPath, new AudioMetadata("FLAC Song", "The Artist", "The Artist", "Mixed Album", 1, 1, 1000));
        metadataReader.Set(m4aPath, new AudioMetadata("M4A Song", "The Artist", "The Artist", "Mixed Album", 2, 1, 1000));
        metadataReader.Set(oggPath, new AudioMetadata("OGG Song", "The Artist", "The Artist", "Mixed Album", 3, 1, 1000));

        var scanner = new LibraryScanner(
            fixture.DbContext,
            metadataReader,
            CreateMediaPathResolver(tempMedia.RootPath, Environments.Production),
            NullLogger<LibraryScanner>.Instance);

        await scanner.ScanAsync(cancellationToken);

        var tracks = await fixture.DbContext.Tracks
            .AsNoTracking()
            .OrderBy(track => track.TrackNumber)
            .ToListAsync(cancellationToken);

        Assert.Equal(3, tracks.Count);
        Assert.Equal(["audio/flac", "audio/mp4", "audio/ogg"], tracks.Select(track => track.MimeType));
        Assert.Equal(["flac", "m4a", "ogg"], tracks.Select(track => track.Format));
    }

    [Fact]
    public async Task ScanAsync_ImportsUntaggedWavAndAiffIntoFilesViewOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var tempMedia = await TempMediaDirectory.CreateAsync();

        var wavPath = tempMedia.CreateFile("Loose/untagged.wav", [1, 2, 3]);
        var aiffPath = tempMedia.CreateFile("Loose/untagged.aiff", [4, 5, 6]);

        var metadataReader = new StubAudioMetadataReader();
        metadataReader.Set(wavPath, new AudioMetadata(null, null, null, null, null, null, null));
        metadataReader.Set(aiffPath, new AudioMetadata(null, null, null, null, null, null, null));

        var scanner = new LibraryScanner(
            fixture.DbContext,
            metadataReader,
            CreateMediaPathResolver(tempMedia.RootPath, Environments.Production),
            NullLogger<LibraryScanner>.Instance);

        await scanner.ScanAsync(cancellationToken);

        var artists = await fixture.DbContext.Artists.AsNoTracking().CountAsync(cancellationToken);
        var albums = await fixture.DbContext.Albums.AsNoTracking().CountAsync(cancellationToken);
        var tracks = await fixture.DbContext.Tracks
            .AsNoTracking()
            .OrderBy(track => track.FileName)
            .ToListAsync(cancellationToken);

        Assert.Equal(0, artists);
        Assert.Equal(0, albums);
        Assert.Equal(2, tracks.Count);
        Assert.Equal(["audio/aiff", "audio/wav"], tracks.Select(track => track.MimeType));
        Assert.Equal(["aiff", "wav"], tracks.Select(track => track.Format));
        Assert.Equal(["untagged", "untagged"], tracks.Select(track => track.Title));
        Assert.All(tracks, track => Assert.Null(track.AlbumId));
    }

    [Fact]
    public async Task ScanAsync_IgnoresDotfilesAndContinuesAfterFileErrors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var tempMedia = await TempMediaDirectory.CreateAsync();

        var goodTrackPath = tempMedia.CreateFile("Album/01-good.mp3", [1, 2, 3]);
        var badTrackPath = tempMedia.CreateFile("Album/02-bad.mp3", [4, 5, 6]);
        var hiddenTrackPath = tempMedia.CreateFile("Album/.hidden.mp3", [7, 8, 9]);

        var metadataReader = new StubAudioMetadataReader();
        metadataReader.Set(goodTrackPath, new AudioMetadata("Good Song", "The Artist", "The Artist", "The Album", 1, 1, 1000));
        metadataReader.SetException(badTrackPath, new InvalidOperationException("bad file"));
        metadataReader.Set(hiddenTrackPath, new AudioMetadata("Hidden Song", "The Artist", "The Artist", "The Album", 99, 1, 1000));

        var scanner = new LibraryScanner(
            fixture.DbContext,
            metadataReader,
            CreateMediaPathResolver(tempMedia.RootPath, Environments.Production),
            NullLogger<LibraryScanner>.Instance);

        await scanner.ScanAsync(cancellationToken);

        var tracks = await fixture.DbContext.Tracks
            .AsNoTracking()
            .OrderBy(track => track.FileName)
            .ToListAsync(cancellationToken);

        Assert.Equal(2, tracks.Count);
        Assert.Equal(["01-good.mp3", "02-bad.mp3"], tracks.Select(track => track.FileName));

        var goodTrack = Assert.Single(tracks, track => track.FileName == "01-good.mp3");
        Assert.Equal("Good Song", goodTrack.Title);
        Assert.NotNull(goodTrack.AlbumId);

        var badTrack = Assert.Single(tracks, track => track.FileName == "02-bad.mp3");
        Assert.Equal("02-bad", badTrack.Title);
        Assert.Null(badTrack.AlbumId);

        Assert.DoesNotContain(tracks, track => track.FileName == ".hidden.mp3");
    }

    [Fact]
    public async Task ScanAsync_ReusesTrackAndAlbumIdsAcrossRescans()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var tempMedia = await TempMediaDirectory.CreateAsync();

        var trackPath = tempMedia.CreateFile("Album/track.mp3", [1, 2, 3, 4]);
        var metadataReader = new StubAudioMetadataReader();
        metadataReader.Set(trackPath, new AudioMetadata("Version One", "The Artist", "The Artist", "The Album", 1, 1, 1000));

        var scanner = new LibraryScanner(
            fixture.DbContext,
            metadataReader,
            CreateMediaPathResolver(tempMedia.RootPath, Environments.Production),
            NullLogger<LibraryScanner>.Instance);

        await scanner.ScanAsync(cancellationToken);

        var originalTrack = await fixture.DbContext.Tracks.AsNoTracking().SingleAsync(cancellationToken);
        var originalAlbum = await fixture.DbContext.Albums.AsNoTracking().SingleAsync(cancellationToken);

        tempMedia.CreateFile("Album/track.mp3", [1, 2, 3, 4, 5], overwrite: true);
        File.SetLastWriteTimeUtc(trackPath, DateTime.UtcNow.AddMinutes(1));
        metadataReader.Set(trackPath, new AudioMetadata("Version Two", "The Artist", "The Artist", "The Album", 1, 1, 1000));

        await scanner.ScanAsync(cancellationToken);

        var rescannedTrack = await fixture.DbContext.Tracks.AsNoTracking().SingleAsync(cancellationToken);
        var rescannedAlbum = await fixture.DbContext.Albums.AsNoTracking().SingleAsync(cancellationToken);

        Assert.Equal(originalTrack.Id, rescannedTrack.Id);
        Assert.Equal(originalAlbum.Id, rescannedAlbum.Id);
        Assert.Equal("Version Two", rescannedTrack.Title);
        Assert.Equal(5, rescannedTrack.FileSize);
    }

    [Fact]
    public void ResolveMediaPath_UsesDevelopmentMediaTestByDefault()
    {
        var resolver = new MediaPathResolver(
            Options.Create(new StorageOptions()),
            new TestHostEnvironment
            {
                ApplicationName = "Phylet",
                EnvironmentName = Environments.Development,
                ContentRootPath = "/tmp/phylet-tests",
                ContentRootFileProvider = new NullFileProvider()
            });

        var mediaPath = resolver.ResolveMediaPath();

        Assert.Equal(Path.GetFullPath("/tmp/phylet-tests/MediaTest"), mediaPath);
    }

    [Fact]
    public void ResolveMediaPath_ThrowsOutsideDevelopmentWhenUnset()
    {
        var resolver = new MediaPathResolver(
            Options.Create(new StorageOptions()),
            new TestHostEnvironment
            {
                ApplicationName = "Phylet",
                EnvironmentName = Environments.Production,
                ContentRootPath = "/tmp/phylet-tests",
                ContentRootFileProvider = new NullFileProvider()
            });

        var exception = Assert.Throws<InvalidOperationException>(() => resolver.ResolveMediaPath());
        Assert.Contains("Storage:MediaPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveMediaFilePath_FindsUnicodeNormalizedPathVariants()
    {
        await using var tempMedia = await TempMediaDirectory.CreateAsync();

        var composedDirectory = "Debussy Piano Works Volume 3_ Images & É";
        var composedFileName = "12 Images oubliées_ Quelques aspects.m4a";
        tempMedia.CreateFile(Path.Combine(composedDirectory, composedFileName), [1, 2, 3]);

        var resolver = CreateMediaPathResolver(tempMedia.RootPath, Environments.Production);
        var decomposedRelativePath = Path.Combine(
                composedDirectory.Normalize(NormalizationForm.FormD),
                composedFileName.Normalize(NormalizationForm.FormD))
            .Replace('\\', '/');

        var resolvedPath = resolver.ResolveMediaFilePath(decomposedRelativePath);

        Assert.True(File.Exists(resolvedPath));
        Assert.Equal(
            Path.Combine(tempMedia.RootPath, composedDirectory, composedFileName).Normalize(NormalizationForm.FormC),
            resolvedPath.Normalize(NormalizationForm.FormC));
    }

    private static MediaPathResolver CreateMediaPathResolver(string mediaPath, string environmentName) =>
        new(
            Options.Create(new StorageOptions { MediaPath = mediaPath }),
            new TestHostEnvironment
            {
                ApplicationName = "Phylet",
                EnvironmentName = environmentName,
                ContentRootPath = mediaPath,
                ContentRootFileProvider = new NullFileProvider()
            });

    private sealed class StubAudioMetadataReader : IAudioMetadataReader
    {
        private readonly Dictionary<string, AudioMetadata> _entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Exception> _exceptions = new(StringComparer.Ordinal);

        public AudioMetadata Read(string filePath)
        {
            if (_exceptions.TryGetValue(filePath, out var exception))
            {
                throw exception;
            }

            return _entries[filePath];
        }

        public void Set(string filePath, AudioMetadata metadata) => _entries[filePath] = metadata;

        public void SetException(string filePath, Exception exception) => _exceptions[filePath] = exception;
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
            var rootPath = Path.Combine(Path.GetTempPath(), $"phylet-media-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return Task.FromResult(new TempMediaDirectory(rootPath));
        }

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(path);
            return path;
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

    private sealed class SqliteInMemoryDbFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SqliteInMemoryDbFixture(SqliteConnection connection, PhyletDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
        }

        public PhyletDbContext DbContext { get; }

        public static async Task<SqliteInMemoryDbFixture> CreateAsync()
        {
            var databaseName = $"phylet-library-tests-{Guid.NewGuid():N}";
            var connection = new SqliteConnection($"Data Source=file:{databaseName}?mode=memory&cache=shared");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<PhyletDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new PhyletDbContext(
                options,
                new RuntimeOptions("Phylet", "Phylet Music Server", 1800));

            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.MigrateAsync();

            return new SqliteInMemoryDbFixture(connection, dbContext);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
