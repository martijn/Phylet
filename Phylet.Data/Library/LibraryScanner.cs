using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Phylet.Data.Configuration;

namespace Phylet.Data.Library;

public sealed class LibraryScanner(
    PhyletDbContext dbContext,
    IAudioMetadataReader metadataReader,
    MediaPathResolver mediaPathResolver,
    ILogger<LibraryScanner> logger)
{
    private static readonly string[] CoverFileNames =
    [
        "cover.jpg",
        "cover.jpeg",
        "cover.png",
        "folder.jpg",
        "folder.jpeg",
        "folder.png",
        "front.jpg",
        "front.jpeg",
        "front.png"
    ];

    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        var mediaRoot = mediaPathResolver.EnsureMediaDirectoryExists();
        logger.LogInformation("Library scan starting. MediaRoot={MediaRoot}", mediaRoot);

        var filePaths = Directory.EnumerateFiles(mediaRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsHiddenFile(path))
            .Where(path => LibraryAudioFormats.TryGetByExtension(Path.GetExtension(path), out _))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var existingArtists = await dbContext.Artists
            .Include(artist => artist.Albums)
            .ToListAsync(cancellationToken);
        var existingAlbums = await dbContext.Albums
            .Include(album => album.Artist)
            .ToListAsync(cancellationToken);
        var existingTracks = await dbContext.Tracks.ToListAsync(cancellationToken);
        var existingFolders = await dbContext.Folders.ToListAsync(cancellationToken);
        var scanState = await dbContext.LibraryScanStates.SingleOrDefaultAsync(state => state.Id == 1, cancellationToken);
        var isNewScanState = scanState is null;
        scanState ??= new LibraryScanState { Id = 1 };

        var artistsByKey = existingArtists.ToDictionary(artist => artist.NormalizedName, StringComparer.Ordinal);
        var albumsByKey = existingAlbums.ToDictionary(BuildAlbumDictionaryKey, StringComparer.Ordinal);
        var tracksByPath = existingTracks.ToDictionary(track => track.RelativePath, StringComparer.Ordinal);
        var foldersByPath = existingFolders.ToDictionary(folder => folder.RelativePath, StringComparer.Ordinal);

        var scannedFiles = filePaths
            .Select(path => CreateScannedFile(mediaRoot, path))
            .ToArray();
        var requiredFolders = CollectFolderPaths(scannedFiles);
        var errorCount = 0;

        EnsureFolders(requiredFolders, foldersByPath);

        foreach (var scannedFile in scannedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var fileInfo = new FileInfo(scannedFile.FullPath);
                var folder = ResolveFolder(scannedFile.DirectoryRelativePath, foldersByPath);
                tracksByPath.TryGetValue(scannedFile.RelativePath, out var trackEntity);
                var audioFormat = LibraryAudioFormats.TryGetByExtension(Path.GetExtension(scannedFile.FileName), out var resolvedFormat)
                    ? resolvedFormat
                    : throw new InvalidOperationException($"Unsupported audio file extension for {scannedFile.RelativePath}");

                if (trackEntity is not null
                    && trackEntity.FileSize == fileInfo.Length
                    && trackEntity.LastModifiedUtc == fileInfo.LastWriteTimeUtc)
                {
                    trackEntity.Folder = folder;
                    continue;
                }

                AudioMetadata? metadata = null;
                try
                {
                    metadata = metadataReader.Read(scannedFile.FullPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to read metadata for {Path}. Track will remain available only via Files view.", scannedFile.RelativePath);
                }

                var hasAlbumMetadata = HasAlbumMetadata(metadata);
                if (trackEntity is null)
                {
                    trackEntity = new TrackEntity
                    {
                        RelativePath = scannedFile.RelativePath
                    };
                    dbContext.Tracks.Add(trackEntity);
                    tracksByPath.Add(trackEntity.RelativePath, trackEntity);
                }

                trackEntity.Folder = folder;
                trackEntity.FileName = scannedFile.FileName;
                trackEntity.Title = hasAlbumMetadata ? metadata!.Title!.Trim() : Path.GetFileNameWithoutExtension(scannedFile.FileName);
                trackEntity.TrackArtistName = NormalizeOptional(metadata?.Artist);
                trackEntity.DiscNumber = metadata?.DiscNumber ?? 0;
                trackEntity.TrackNumber = metadata?.TrackNumber ?? 0;
                trackEntity.Format = audioFormat.Format;
                trackEntity.MimeType = audioFormat.MimeType;
                trackEntity.FileSize = fileInfo.Length;
                trackEntity.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
                trackEntity.DurationMs = metadata?.DurationMs;

                if (hasAlbumMetadata)
                {
                    var albumArtistName = NormalizeRequired(metadata!.AlbumArtist ?? metadata.Artist!);
                    var artist = ResolveArtist(albumArtistName, artistsByKey);
                    var album = ResolveAlbum(
                        artist,
                        NormalizeRequired(metadata.Album!),
                        scannedFile.DirectoryRelativePath,
                        albumsByKey);
                    trackEntity.Album = album;
                }
                else
                {
                    trackEntity.Album = null;
                    trackEntity.AlbumId = null;
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                logger.LogWarning(ex, "Failed to scan media file {Path}. Continuing library scan.", scannedFile.RelativePath);
            }
        }

        var currentRelativePaths = scannedFiles.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var staleTrack in existingTracks.Where(track => !currentRelativePaths.Contains(track.RelativePath)))
        {
            dbContext.Tracks.Remove(staleTrack);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var activeAlbumIds = await dbContext.Tracks
            .Where(track => track.AlbumId.HasValue)
            .Select(track => track.AlbumId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var orphanAlbums = await dbContext.Albums
            .Where(album => !activeAlbumIds.Contains(album.Id))
            .ToListAsync(cancellationToken);
        if (orphanAlbums.Count > 0)
        {
            dbContext.Albums.RemoveRange(orphanAlbums);
        }

        var activeArtistIds = await dbContext.Albums
            .Select(album => album.ArtistId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var orphanArtists = await dbContext.Artists
            .Where(artist => !activeArtistIds.Contains(artist.Id))
            .ToListAsync(cancellationToken);
        if (orphanArtists.Count > 0)
        {
            dbContext.Artists.RemoveRange(orphanArtists);
        }

        var staleFolders = await dbContext.Folders
            .Where(folder => !requiredFolders.Contains(folder.RelativePath))
            .ToListAsync(cancellationToken);
        if (staleFolders.Count > 0)
        {
            dbContext.Folders.RemoveRange(staleFolders);
        }

        var remainingAlbums = await dbContext.Albums.ToListAsync(cancellationToken);
        foreach (var album in remainingAlbums)
        {
            album.CoverRelativePath = ResolveCoverRelativePath(mediaRoot, album.AlbumPathKey);
        }

        scanState.LastScanUtc = DateTime.UtcNow;
        scanState.LastError = null;
        if (isNewScanState)
        {
            dbContext.LibraryScanStates.Add(scanState);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Library scan completed. MediaRoot={MediaRoot}, FilesDiscovered={DiscoveredFileCount}, Errors={ErrorCount}, Tracks={TrackCount}, Albums={AlbumCount}, Artists={ArtistCount}, Folders={FolderCount}",
            mediaRoot,
            scannedFiles.Length,
            errorCount,
            await dbContext.Tracks.CountAsync(cancellationToken),
            await dbContext.Albums.CountAsync(cancellationToken),
            await dbContext.Artists.CountAsync(cancellationToken),
            await dbContext.Folders.CountAsync(cancellationToken));
    }

    private static ScannedFile CreateScannedFile(string mediaRoot, string fullPath)
    {
        var relativePath = NormalizeRelativePath(Path.GetRelativePath(mediaRoot, fullPath));
        var directoryRelativePath = NormalizeRelativePath(Path.GetDirectoryName(relativePath) ?? string.Empty);
        return new ScannedFile(fullPath, relativePath, directoryRelativePath, Path.GetFileName(fullPath));
    }

    private static bool IsHiddenFile(string path) =>
        Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal);

    private static HashSet<string> CollectFolderPaths(IEnumerable<ScannedFile> scannedFiles)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scannedFile in scannedFiles)
        {
            foreach (var folderPath in EnumerateAncestorFolders(scannedFile.DirectoryRelativePath))
            {
                result.Add(folderPath);
            }
        }

        return result;
    }

    private void EnsureFolders(IReadOnlyCollection<string> requiredFolders, IDictionary<string, FolderEntity> foldersByPath)
    {
        foreach (var folderPath in requiredFolders.OrderBy(path => path.Count(ch => ch == '/')).ThenBy(path => path, StringComparer.Ordinal))
        {
            if (foldersByPath.ContainsKey(folderPath))
            {
                continue;
            }

            var parentPath = GetParentRelativePath(folderPath);
            var folder = new FolderEntity
            {
                RelativePath = folderPath,
                Name = Path.GetFileName(folderPath),
                ParentFolder = ResolveFolder(parentPath, foldersByPath)
            };
            dbContext.Folders.Add(folder);
            foldersByPath.Add(folder.RelativePath, folder);
        }
    }

    private static FolderEntity? ResolveFolder(string relativePath, IDictionary<string, FolderEntity> foldersByPath) =>
        string.IsNullOrEmpty(relativePath)
            ? null
            : foldersByPath[relativePath];

    private ArtistEntity ResolveArtist(string artistName, IDictionary<string, ArtistEntity> artistsByKey)
    {
        var normalizedName = NormalizeKey(artistName);
        if (artistsByKey.TryGetValue(normalizedName, out var artist))
        {
            if (!string.Equals(artist.Name, artistName, StringComparison.Ordinal))
            {
                artist.Name = artistName;
            }

            return artist;
        }

        artist = new ArtistEntity
        {
            Name = artistName,
            NormalizedName = normalizedName
        };
        dbContext.Artists.Add(artist);
        artistsByKey.Add(normalizedName, artist);
        return artist;
    }

    private AlbumEntity ResolveAlbum(
        ArtistEntity artist,
        string albumTitle,
        string albumPathKey,
        IDictionary<string, AlbumEntity> albumsByKey)
    {
        var album = new AlbumEntity
        {
            Artist = artist,
            Title = albumTitle,
            NormalizedTitle = NormalizeKey(albumTitle),
            AlbumPathKey = albumPathKey
        };
        var albumKey = BuildAlbumDictionaryKey(album);
        if (albumsByKey.TryGetValue(albumKey, out var existingAlbum))
        {
            if (!string.Equals(existingAlbum.Title, albumTitle, StringComparison.Ordinal))
            {
                existingAlbum.Title = albumTitle;
            }

            return existingAlbum;
        }

        dbContext.Albums.Add(album);
        albumsByKey.Add(albumKey, album);
        return album;
    }

    private string? ResolveCoverRelativePath(string mediaRoot, string albumPathKey)
    {
        var albumDirectory = mediaPathResolver.ResolveMediaFilePath(albumPathKey);
        var entries = Directory.Exists(albumDirectory)
            ? Directory.EnumerateFiles(albumDirectory, "*", SearchOption.TopDirectoryOnly).ToArray()
            : [];

        foreach (var expectedName in CoverFileNames)
        {
            var coverPath = entries.FirstOrDefault(path => string.Equals(Path.GetFileName(path), expectedName, StringComparison.OrdinalIgnoreCase));
            if (coverPath is not null)
            {
                return NormalizeRelativePath(Path.GetRelativePath(mediaRoot, coverPath));
            }
        }

        return null;
    }

    private static bool HasAlbumMetadata(AudioMetadata? metadata) =>
        metadata is not null
        && !string.IsNullOrWhiteSpace(metadata.Title)
        && !string.IsNullOrWhiteSpace(metadata.Album)
        && metadata.TrackNumber is > 0
        && (!string.IsNullOrWhiteSpace(metadata.AlbumArtist) || !string.IsNullOrWhiteSpace(metadata.Artist));

    private static string NormalizeRequired(string value) => value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeKey(string value) => value.Trim().ToUpperInvariant();

    private static string BuildAlbumDictionaryKey(AlbumEntity album) =>
        $"{album.Artist.NormalizedName}|{album.NormalizedTitle}|{album.AlbumPathKey}";

    private static IEnumerable<string> EnumerateAncestorFolders(string relativeDirectoryPath)
    {
        if (string.IsNullOrEmpty(relativeDirectoryPath))
        {
            yield break;
        }

        var parts = relativeDirectoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            yield return string.Join('/', parts.Take(i + 1));
        }
    }

    private static string GetParentRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return string.Empty;
        }

        var lastSeparator = relativePath.LastIndexOf('/');
        return lastSeparator >= 0 ? relativePath[..lastSeparator] : string.Empty;
    }

    private static string NormalizeRelativePath(string relativePath) =>
        string.IsNullOrEmpty(relativePath)
            ? string.Empty
            : relativePath.Replace('\\', '/');

    private sealed record ScannedFile(
        string FullPath,
        string RelativePath,
        string DirectoryRelativePath,
        string FileName);
}
