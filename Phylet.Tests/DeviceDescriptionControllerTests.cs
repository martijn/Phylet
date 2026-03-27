using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Phylet.Controllers;
using Phylet.Data;
using Phylet.Data.Configuration;
using Xunit;

namespace Phylet.Tests;

public sealed class DeviceDescriptionControllerTests
{
    [Fact]
    public async Task Get_UsesRuntimeConfigurationLoadedFromSqlite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await SqliteInMemoryDbFixture.CreateAsync();

        fixture.DbContext.DeviceConfigurations.AddRange(
            new DeviceConfigurationEntry
            {
                Key = DeviceConfigurationDefaults.DeviceUuidKey,
                Value = "uuid:22222222-2222-2222-2222-222222222222"
            },
            new DeviceConfigurationEntry
            {
                Key = DeviceConfigurationDefaults.FriendlyNameKey,
                Value = "Kitchen"
            });
        await fixture.DbContext.SaveChangesAsync(cancellationToken);

        var provider = new RuntimeDeviceConfigurationProvider();
        var initializer = new DeviceConfigurationInitializer(
            fixture.DbContext,
            provider,
            NullLogger<DeviceConfigurationInitializer>.Instance);
        await initializer.InitializeAsync(cancellationToken);

        var controller = new DeviceDescriptionController(provider)
        {
            ControllerContext = new()
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Request.Scheme = "http";
        controller.HttpContext.Request.Host = new HostString("127.0.0.1:55128");

        var result = controller.Get();
        var xml = XDocument.Parse(result.Content!);
        var friendlyName = xml.Descendants().First(node => node.Name.LocalName == "friendlyName").Value;
        var udn = xml.Descendants().First(node => node.Name.LocalName == "UDN").Value;
        var iconUrls = xml.Descendants()
            .Where(node => node.Name.LocalName == "url")
            .Select(node => node.Value)
            .ToArray();

        Assert.Equal("Kitchen", friendlyName);
        Assert.Equal("uuid:22222222-2222-2222-2222-222222222222", udn);
        Assert.Equal(
            ["/icons/server-48.png", "/icons/server-120.png", "/icons/server-240.png"],
            iconUrls);
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
            var databaseName = $"phylet-host-tests-{Guid.NewGuid():N}";
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
