using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text;

namespace Phylet.Data.Configuration;

public sealed class MediaPathResolver(IOptions<StorageOptions> options, IHostEnvironment environment)
{
    public string ResolveMediaPath()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.MediaPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.Value.MediaPath));
        }

        if (string.Equals(environment.EnvironmentName, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "MediaTest"));
        }

        throw new InvalidOperationException("Storage:MediaPath must be configured outside Development.");
    }

    public string ResolveMediaFilePath(string relativePath)
    {
        var mediaRoot = EnsureMediaDirectoryExists();
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(mediaRoot, normalizedRelativePath));
        var rootWithSeparator = mediaRoot.EndsWith(Path.DirectorySeparatorChar)
            ? mediaRoot
            : mediaRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal) && !string.Equals(fullPath, mediaRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Resolved media path escapes the configured media root. RelativePath={relativePath}");
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            return fullPath;
        }

        var resolvedExistingPath = TryResolveExistingPath(mediaRoot, normalizedRelativePath);
        if (resolvedExistingPath is not null)
        {
            return resolvedExistingPath;
        }

        return fullPath;
    }

    public string EnsureMediaDirectoryExists()
    {
        var mediaPath = ResolveMediaPath();
        if (!Directory.Exists(mediaPath))
        {
            throw new InvalidOperationException($"Configured media path does not exist: {mediaPath}");
        }

        return mediaPath;
    }

    private static string NormalizeRelativePath(string relativePath) =>
        string.IsNullOrEmpty(relativePath)
            ? string.Empty
            : relativePath.Replace('\\', '/');

    private static string? TryResolveExistingPath(string mediaRoot, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return mediaRoot;
        }

        var currentPath = mediaRoot;
        foreach (var segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(currentPath))
            {
                return null;
            }

            var nextPath = Directory
                .EnumerateFileSystemEntries(currentPath, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => SegmentMatches(Path.GetFileName(path), segment));

            if (nextPath is null)
            {
                return null;
            }

            currentPath = nextPath;
        }

        return currentPath;
    }

    private static bool SegmentMatches(string candidate, string requested) =>
        string.Equals(candidate, requested, StringComparison.Ordinal)
        || string.Equals(
            candidate.Normalize(NormalizationForm.FormC),
            requested.Normalize(NormalizationForm.FormC),
            StringComparison.Ordinal);
}
