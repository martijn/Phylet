using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Phylet.Controllers;
using Phylet.Data;
using Phylet.Data.Configuration;
using Phylet.Data.Library;
using Xunit;

namespace Phylet.Tests;

public sealed class MediaControllerTests
{
    [Fact]
    public async Task Image_ServesEmbeddedArtworkWhenIndexedReferenceExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var mediaRoot = await TempMediaDirectory.CreateAsync();

        mediaRoot.CreateFile("Embedded Album/track-1.flac", [1, 2, 3]);
        var albumId = await SeedAlbumWithEmbeddedArtworkAsync(fixture.DbContext, cancellationToken);

        var metadataReader = new StubAudioMetadataReader();
        metadataReader.SetEmbeddedArtwork(
            Path.Combine(mediaRoot.RootPath, "Embedded Album/track-1.flac"),
            new EmbeddedArtworkContent("image/png", [9, 8, 7, 6]));

        var controller = CreateController(fixture.Connection, mediaRoot.RootPath, metadataReader);

        var result = await controller.Image(albumId);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/png", fileResult.ContentType);
        Assert.Equal([9, 8, 7, 6], fileResult.FileContents);
        Assert.Equal("Interactive", controller.Response.Headers["transferMode.dlna.org"]);
        Assert.Equal(
            "DLNA.ORG_PN=PNG_TN;DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000",
            controller.Response.Headers["contentFeatures.dlna.org"]);
    }

    [Fact]
    public async Task Image_ReturnsNotFoundWhenEmbeddedArtworkExtractionFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var mediaRoot = await TempMediaDirectory.CreateAsync();

        mediaRoot.CreateFile("Embedded Album/track-1.flac", [1, 2, 3]);
        var albumId = await SeedAlbumWithEmbeddedArtworkAsync(fixture.DbContext, cancellationToken);

        var metadataReader = new StubAudioMetadataReader();
        metadataReader.SetException(
            Path.Combine(mediaRoot.RootPath, "Embedded Album/track-1.flac"),
            new InvalidOperationException("bad art"));

        var controller = CreateController(fixture.Connection, mediaRoot.RootPath, metadataReader);

        var result = await controller.Image(albumId);

        Assert.IsType<NotFoundResult>(result);
    }

    private static MediaController CreateController(
        SqliteConnection connection,
        string mediaPath,
        IAudioMetadataReader metadataReader)
    {
        var controller = new MediaController(
            new LibraryService(
                new TestDbContextFactory(connection),
                new MediaPathResolver(
                    Options.Create(new StorageOptions { MediaPath = mediaPath }),
                    new TestHostEnvironment
                    {
                        ApplicationName = "Phylet",
                        EnvironmentName = Environments.Production,
                        ContentRootPath = mediaPath,
                        ContentRootFileProvider = new NullFileProvider()
                    })),
            metadataReader,
            NullLogger<MediaController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return controller;
    }

    private static async Task<int> SeedAlbumWithEmbeddedArtworkAsync(PhyletDbContext dbContext, CancellationToken cancellationToken)
    {
        var artist = new ArtistEntity
        {
            Name = "Embedded Artist",
            NormalizedName = "EMBEDDED ARTIST"
        };
        var album = new AlbumEntity
        {
            Artist = artist,
            Title = "Embedded Album",
            NormalizedTitle = "EMBEDDED ALBUM",
            AlbumPathKey = "Embedded Album",
            EmbeddedCoverRelativePath = "Embedded Album/track-1.flac",
            EmbeddedCoverMimeType = "image/png"
        };

        dbContext.Artists.Add(artist);
        dbContext.Albums.Add(album);
        await dbContext.SaveChangesAsync(cancellationToken);
        return album.Id;
    }

    private sealed class StubAudioMetadataReader : IAudioMetadataReader
    {
        private readonly Dictionary<string, EmbeddedArtworkContent> _embeddedArtwork = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Exception> _exceptions = new(StringComparer.Ordinal);

        public AudioMetadata Read(string filePath) => throw new NotSupportedException();

        public EmbeddedArtworkContent? ReadEmbeddedArtwork(string filePath, int maxArtworkBytes)
        {
            if (_exceptions.TryGetValue(filePath, out var exception))
            {
                throw exception;
            }

            if (!_embeddedArtwork.TryGetValue(filePath, out var artwork))
            {
                return null;
            }

            return artwork.Data.Length <= maxArtworkBytes ? artwork : null;
        }

        public void SetEmbeddedArtwork(string filePath, EmbeddedArtworkContent artwork) => _embeddedArtwork[filePath] = artwork;

        public void SetException(string filePath, Exception exception) => _exceptions[filePath] = exception;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Phylet";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<PhyletDbContext>
    {
        public PhyletDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<PhyletDbContext>()
                .UseSqlite(connection)
                .Options;

            return new PhyletDbContext(
                options,
                new RuntimeOptions("Phylet", "Phylet Music Server", 1800));
        }

        public Task<PhyletDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TempMediaDirectory : IAsyncDisposable
    {
        private TempMediaDirectory(string rootPath) => RootPath = rootPath;

        public string RootPath { get; }

        public static Task<TempMediaDirectory> CreateAsync()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), $"phylet-media-controller-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return Task.FromResult(new TempMediaDirectory(rootPath));
        }

        public string CreateFile(string relativePath, byte[] contents)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
        private SqliteInMemoryDbFixture(SqliteConnection connection, PhyletDbContext dbContext)
        {
            Connection = connection;
            DbContext = dbContext;
        }

        public SqliteConnection Connection { get; }
        public PhyletDbContext DbContext { get; }

        public static async Task<SqliteInMemoryDbFixture> CreateAsync()
        {
            var databaseName = $"phylet-media-controller-{Guid.NewGuid():N}";
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
            await Connection.DisposeAsync();
        }
    }
}
