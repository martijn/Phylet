using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Phylet.Data.Configuration;

public sealed class DatabasePathResolver(IOptions<StorageOptions> options, IHostEnvironment environment)
{
    public string ResolveDatabasePath()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.DatabasePath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.Value.DatabasePath));
        }

        var baseDirectory = ResolveDefaultBaseDirectory();
        return Path.Combine(baseDirectory, environment.ApplicationName, "phylet.db");
    }

    private static string ResolveDefaultBaseDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDirectory, "Library", "Application Support");
        }

        if (OperatingSystem.IsLinux())
        {
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgDataHome))
            {
                return xdgDataHome;
            }

            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDirectory, ".local", "share");
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }
}
