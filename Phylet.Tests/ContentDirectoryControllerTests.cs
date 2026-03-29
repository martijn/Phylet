using System.Text;
using System.Xml.Linq;
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
using Phylet.Services;
using Xunit;

namespace Phylet.Tests;

public sealed class ContentDirectoryControllerTests
{
    [Fact]
    public async Task Control_BrowseRoot_ReturnsArtistsAlbumsAndFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var mediaRoot = await TempMediaDirectory.CreateAsync();

        await SeedLibraryAsync(fixture.DbContext, cancellationToken);

        var provider = new RuntimeDeviceConfigurationProvider();
        provider.Set(new RuntimeDeviceConfiguration(
            "uuid:33333333-3333-3333-3333-333333333333",
            "Test",
            "Phylet",
            "Phylet Music Server",
            1800));

        var controller = CreateController(fixture.Connection, mediaRoot.RootPath, provider);
        SetBrowseRequest(controller, "0", "BrowseDirectChildren");

        var result = await controller.Control();
        var titles = ExtractDidlTitles(result.Content!);

        Assert.Equal(["Artists", "Albums", "Files"], titles);
    }

    [Fact]
    public async Task Control_BrowseAlbum_ReturnsTracksInDiscAndTrackOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var mediaRoot = await TempMediaDirectory.CreateAsync();

        var albumId = await SeedLibraryAsync(fixture.DbContext, cancellationToken);
        var provider = new RuntimeDeviceConfigurationProvider();
        provider.Set(new RuntimeDeviceConfiguration(
            "uuid:44444444-4444-4444-4444-444444444444",
            "Test",
            "Phylet",
            "Phylet Music Server",
            1800));

        var controller = CreateController(fixture.Connection, mediaRoot.RootPath, provider);
        SetBrowseRequest(controller, $"album:{albumId}", "BrowseDirectChildren");

        var result = await controller.Control();
        var titles = ExtractDidlTitles(result.Content!);

        Assert.Equal(["2. Disc 1 Track 2", "1. Disc 2 Track 1"], titles);
    }

    [Fact]
    public async Task Control_BrowseArtistsAndAlbums_ReturnsAlphabeticalOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var mediaRoot = await TempMediaDirectory.CreateAsync();

        await SeedAlphabeticalLibraryAsync(fixture.DbContext, cancellationToken);

        var provider = new RuntimeDeviceConfigurationProvider();
        provider.Set(new RuntimeDeviceConfiguration(
            "uuid:55555555-5555-5555-5555-555555555555",
            "Test",
            "Phylet",
            "Phylet Music Server",
            1800));

        var controller = CreateController(fixture.Connection, mediaRoot.RootPath, provider);

        SetBrowseRequest(controller, "artists", "BrowseDirectChildren");
        var artistsResult = await controller.Control();
        Assert.Equal(["Alpha Artist", "zeta artist"], ExtractDidlTitles(artistsResult.Content!));

        SetBrowseRequest(controller, "albums", "BrowseDirectChildren");
        var albumsResult = await controller.Control();
        Assert.Equal(["Alpha Album", "zebra Album"], ExtractDidlTitles(albumsResult.Content!));
    }

    [Fact]
    public async Task Control_BrowseFilesRoot_UsesFolderAndFileTrackObjectIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var mediaRoot = await TempMediaDirectory.CreateAsync();

        await SeedLibraryAsync(fixture.DbContext, cancellationToken);

        var provider = new RuntimeDeviceConfigurationProvider();
        provider.Set(new RuntimeDeviceConfiguration(
            "uuid:66666666-6666-6666-6666-666666666666",
            "Test",
            "Phylet",
            "Phylet Music Server",
            1800));

        var controller = CreateController(fixture.Connection, mediaRoot.RootPath, provider);
        SetBrowseRequest(controller, "files", "BrowseDirectChildren");

        var result = await controller.Control();
        var ids = ExtractDidlObjectIds(result.Content!);

        Assert.Equal(2, ids.Length);
        Assert.Equal("folder:1", ids[0]);
        Assert.StartsWith("file-track:", ids[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Control_BrowseAlbums_UsesEmbeddedArtworkProfileWhenNoCoverFileExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();
        await using var mediaRoot = await TempMediaDirectory.CreateAsync();

        await SeedLibraryWithEmbeddedArtworkAsync(fixture.DbContext, cancellationToken);

        var provider = new RuntimeDeviceConfigurationProvider();
        provider.Set(new RuntimeDeviceConfiguration(
            "uuid:77777777-7777-7777-7777-777777777777",
            "Test",
            "Phylet",
            "Phylet Music Server",
            1800));

        var controller = CreateController(fixture.Connection, mediaRoot.RootPath, provider);
        SetBrowseRequest(controller, "albums", "BrowseDirectChildren");

        var result = await controller.Control();
        var soap = XDocument.Parse(result.Content!);
        var didl = XDocument.Parse(soap.Descendants().First(element => element.Name.LocalName == "Result").Value);
        var artElement = didl.Descendants().First(element => element.Name.LocalName == "albumArtURI");

        Assert.Equal("http://127.0.0.1:5128/media/image/1", artElement.Value);
        Assert.Equal(
            LibraryPresentation.PngAlbumArtProfileId,
            artElement.Attributes().First(attribute => attribute.Name.LocalName == "profileID").Value);
    }

    private static ContentDirectoryController CreateController(
        SqliteConnection connection,
        string mediaPath,
        RuntimeDeviceConfigurationProvider provider)
    {
        var dbContextFactory = new TestDbContextFactory(connection);
        var library = new LibraryService(
            dbContextFactory,
            new MediaPathResolver(
                Options.Create(new StorageOptions { MediaPath = mediaPath }),
                new TestHostEnvironment
                {
                    ApplicationName = "Phylet",
                    EnvironmentName = Environments.Production,
                    ContentRootPath = mediaPath,
                    ContentRootFileProvider = new NullFileProvider()
                }));

        var controller = new ContentDirectoryController(
            library,
            new DidlBuilder(),
            new EventSubscriptionService(provider, TimeProvider.System),
            NullLogger<ContentDirectoryController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Request.Scheme = "http";
        controller.HttpContext.Request.Host = new HostString("127.0.0.1:5128");
        return controller;
    }

    private static void SetBrowseRequest(ContentDirectoryController controller, string objectId, string browseFlag)
    {
        controller.HttpContext.Request.Headers["SOAPACTION"] = "\"urn:schemas-upnp-org:service:ContentDirectory:1#Browse\"";
        var body = $$"""
                     <?xml version="1.0" encoding="utf-8"?>
                     <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
                       <s:Body>
                         <u:Browse xmlns:u="urn:schemas-upnp-org:service:ContentDirectory:1">
                           <ObjectID>{{objectId}}</ObjectID>
                           <BrowseFlag>{{browseFlag}}</BrowseFlag>
                           <Filter>*</Filter>
                           <StartingIndex>0</StartingIndex>
                           <RequestedCount>0</RequestedCount>
                           <SortCriteria></SortCriteria>
                         </u:Browse>
                       </s:Body>
                     </s:Envelope>
                     """;
        controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
    }

    private static string[] ExtractDidlTitles(string soapContent)
    {
        var soap = XDocument.Parse(soapContent);
        var resultElement = soap.Descendants().First(element => element.Name.LocalName == "Result");
        var didl = XDocument.Parse(resultElement.Value);
        return didl.Descendants()
            .Where(element => element.Name.LocalName == "title")
            .Select(element => element.Value)
            .ToArray();
    }

    private static string[] ExtractDidlObjectIds(string soapContent)
    {
        var soap = XDocument.Parse(soapContent);
        var resultElement = soap.Descendants().First(element => element.Name.LocalName == "Result");
        var didl = XDocument.Parse(resultElement.Value);
        return didl.Root!.Elements()
            .Select(element => element.Attribute("id")!.Value)
            .ToArray();
    }

    private static async Task<int> SeedLibraryAsync(PhyletDbContext dbContext, CancellationToken cancellationToken)
    {
        var artist = new ArtistEntity
        {
            Name = "The Artist",
            NormalizedName = "THE ARTIST"
        };
        var album = new AlbumEntity
        {
            Artist = artist,
            Title = "The Album",
            NormalizedTitle = "THE ALBUM",
            AlbumPathKey = "The Album",
            CoverRelativePath = "The Album/cover.jpg"
        };
        var folder = new FolderEntity
        {
            RelativePath = "The Album",
            Name = "The Album"
        };

        dbContext.Artists.Add(artist);
        dbContext.Albums.Add(album);
        dbContext.Folders.Add(folder);
        dbContext.Tracks.AddRange(
            new TrackEntity
            {
                Album = album,
                Folder = folder,
                RelativePath = "The Album/track-2.mp3",
                FileName = "track-2.mp3",
                Title = "Disc 1 Track 2",
                TrackArtistName = "The Artist",
                DiscNumber = 1,
                TrackNumber = 2,
                Format = "mp3",
                MimeType = "audio/mpeg",
                FileSize = 12,
                LastModifiedUtc = DateTime.UtcNow
            },
            new TrackEntity
            {
                Album = album,
                Folder = folder,
                RelativePath = "The Album/track-1.mp3",
                FileName = "track-1.mp3",
                Title = "Disc 2 Track 1",
                TrackArtistName = "The Artist",
                DiscNumber = 2,
                TrackNumber = 1,
                Format = "mp3",
                MimeType = "audio/mpeg",
                FileSize = 13,
                LastModifiedUtc = DateTime.UtcNow
            },
            new TrackEntity
            {
                RelativePath = "loose-file.mp3",
                FileName = "loose-file.mp3",
                Title = "Loose File",
                DiscNumber = 0,
                TrackNumber = 0,
                Format = "mp3",
                MimeType = "audio/mpeg",
                FileSize = 14,
                LastModifiedUtc = DateTime.UtcNow
            });

        await dbContext.SaveChangesAsync(cancellationToken);
        return album.Id;
    }

    private static async Task SeedAlphabeticalLibraryAsync(PhyletDbContext dbContext, CancellationToken cancellationToken)
    {
        var alphaArtist = new ArtistEntity
        {
            Name = "Alpha Artist",
            NormalizedName = "ALPHA ARTIST"
        };
        var zetaArtist = new ArtistEntity
        {
            Name = "zeta artist",
            NormalizedName = "ZETA ARTIST"
        };

        dbContext.Artists.AddRange(alphaArtist, zetaArtist);
        dbContext.Albums.AddRange(
            new AlbumEntity
            {
                Artist = zetaArtist,
                Title = "zebra Album",
                NormalizedTitle = "ZEBRA ALBUM",
                AlbumPathKey = "zeta artist/zebra Album"
            },
            new AlbumEntity
            {
                Artist = alphaArtist,
                Title = "Alpha Album",
                NormalizedTitle = "ALPHA ALBUM",
                AlbumPathKey = "Alpha Artist/Alpha Album"
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedLibraryWithEmbeddedArtworkAsync(PhyletDbContext dbContext, CancellationToken cancellationToken)
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
        dbContext.Tracks.Add(new TrackEntity
        {
            Album = album,
            RelativePath = "Embedded Album/track-1.flac",
            FileName = "track-1.flac",
            Title = "Track",
            TrackArtistName = "Embedded Artist",
            DiscNumber = 1,
            TrackNumber = 1,
            Format = "flac",
            MimeType = "audio/flac",
            FileSize = 12,
            LastModifiedUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
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
            var rootPath = Path.Combine(Path.GetTempPath(), $"phylet-host-media-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return Task.FromResult(new TempMediaDirectory(rootPath));
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
            var databaseName = $"phylet-content-tests-{Guid.NewGuid():N}";
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
