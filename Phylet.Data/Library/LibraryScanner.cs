using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Phylet.Data.Configuration;

namespace Phylet.Data.Library;

public sealed class LibraryScanner(
    PhyletDbContext dbContext,
    IAudioMetadataReader metadataReader,
    ICueSheetParser cueSheetParser,
    IAudioDecoder audioDecoder,
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

        var discoveredPaths = Directory.EnumerateFiles(mediaRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsHiddenFile(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var audioFiles = discoveredPaths
            .Where(path => LibraryAudioFormats.TryGetByExtension(Path.GetExtension(path), out _))
            .Select(path => CreateScannedFile(mediaRoot, path))
            .ToArray();
        var cueFiles = discoveredPaths
            .Where(path => string.Equals(Path.GetExtension(path), ".cue", StringComparison.OrdinalIgnoreCase))
            .Select(path => CreateScannedFile(mediaRoot, path))
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
        var claimedAudioPaths = new HashSet<string>(StringComparer.Ordinal);
        var errorCount = 0;

        var cueTracks = LoadCueTracks(mediaRoot, cueFiles, claimedAudioPaths, ref errorCount);
        var directAudioFiles = audioFiles
            .Where(file => !claimedAudioPaths.Contains(file.RelativePath))
            .ToArray();
        var requiredFolders = CollectFolderPaths(
            directAudioFiles.Select(file => file.DirectoryRelativePath)
                .Concat(cueTracks.Select(track => track.DirectoryRelativePath)));

        EnsureFolders(requiredFolders, foldersByPath);

        foreach (var cueTrack in cueTracks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var folder = ResolveFolder(cueTrack.DirectoryRelativePath, foldersByPath);
                tracksByPath.TryGetValue(cueTrack.RelativePath, out var trackEntity);
                trackEntity ??= CreateTrackEntity(cueTrack.RelativePath, tracksByPath);

                ApplyCueTrack(trackEntity, cueTrack, folder, artistsByKey, albumsByKey);
            }
            catch (Exception ex)
            {
                errorCount++;
                logger.LogWarning(ex, "Failed to index cue track {Path}. Continuing library scan.", cueTrack.RelativePath);
            }
        }

        foreach (var scannedFile in directAudioFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var fileInfo = new FileInfo(scannedFile.FullPath);
                var folder = ResolveFolder(scannedFile.DirectoryRelativePath, foldersByPath);
                tracksByPath.TryGetValue(scannedFile.RelativePath, out var trackEntity);
                var audioFormat = LibraryAudioFormats.ResolveByExtension(Path.GetExtension(scannedFile.FileName));

                if (trackEntity is not null
                    && trackEntity.SourceKind is TrackSourceKind.File
                    && trackEntity.FileSize == fileInfo.Length
                    && trackEntity.LastModifiedUtc == fileInfo.LastWriteTimeUtc)
                {
                    trackEntity.Folder = folder;
                    trackEntity.SourceRelativePath = scannedFile.RelativePath;
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

                trackEntity ??= CreateTrackEntity(scannedFile.RelativePath, tracksByPath);
                var hasAlbumMetadata = HasAlbumMetadata(metadata);

                trackEntity.SourceKind = TrackSourceKind.File;
                trackEntity.SourceRelativePath = scannedFile.RelativePath;
                trackEntity.CueSheetRelativePath = null;
                trackEntity.CueSegmentStartMs = null;
                trackEntity.CueSegmentDurationMs = null;
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
                    AssignAlbum(
                        trackEntity,
                        NormalizeRequired(metadata!.AlbumArtist ?? metadata.Artist!),
                        NormalizeRequired(metadata.Album!),
                        scannedFile.DirectoryRelativePath,
                        artistsByKey,
                        albumsByKey);
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

        var currentRelativePaths = cueTracks.Select(track => track.RelativePath)
            .Concat(directAudioFiles.Select(file => file.RelativePath))
            .ToHashSet(StringComparer.Ordinal);
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
        var embeddedArtworkCandidates = await dbContext.Tracks
            .Where(track => track.AlbumId.HasValue)
            .OrderBy(track => track.AlbumId)
            .ThenBy(track => track.RelativePath)
            .Select(track => new { AlbumId = track.AlbumId!.Value, track.SourceRelativePath })
            .ToListAsync(cancellationToken);
        var embeddedArtworkSourceByAlbumId = embeddedArtworkCandidates
            .GroupBy(candidate => candidate.AlbumId)
            .ToDictionary(group => group.Key, group => group.First().SourceRelativePath);

        foreach (var album in remainingAlbums)
        {
            album.CoverRelativePath = ResolveCoverRelativePath(mediaRoot, album.AlbumPathKey);
            album.EmbeddedCoverRelativePath = null;
            album.EmbeddedCoverMimeType = null;

            if (album.CoverRelativePath is not null
                || !embeddedArtworkSourceByAlbumId.TryGetValue(album.Id, out var artworkSourceRelativePath))
            {
                continue;
            }

            try
            {
                var embeddedArtwork = metadataReader.ReadEmbeddedArtwork(
                    mediaPathResolver.ResolveMediaFilePath(artworkSourceRelativePath),
                    LibraryPresentation.MaxEmbeddedArtworkBytes);
                if (embeddedArtwork is null)
                {
                    continue;
                }

                album.EmbeddedCoverRelativePath = artworkSourceRelativePath;
                album.EmbeddedCoverMimeType = embeddedArtwork.MimeType;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to inspect embedded artwork for album {AlbumId} from {Path}.", album.Id, artworkSourceRelativePath);
            }
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
            directAudioFiles.Length + cueTracks.Count,
            errorCount,
            await dbContext.Tracks.CountAsync(cancellationToken),
            await dbContext.Albums.CountAsync(cancellationToken),
            await dbContext.Artists.CountAsync(cancellationToken),
            await dbContext.Folders.CountAsync(cancellationToken));
    }

    private List<CueTrackImport> LoadCueTracks(
        string mediaRoot,
        IReadOnlyList<ScannedFile> cueFiles,
        ISet<string> claimedAudioPaths,
        ref int errorCount)
    {
        var result = new List<CueTrackImport>();
        if (cueFiles.Count == 0)
        {
            return result;
        }

        var availability = audioDecoder.GetAvailability();
        if (!availability.IsAvailable)
        {
            logger.LogInformation("Cue sheet support disabled for this scan: {Reason}", availability.Reason);
            return result;
        }

        foreach (var cueFile in cueFiles)
        {
            try
            {
                var cueSheet = cueSheetParser.Parse(cueFile.FullPath);
                var sourceFullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cueFile.FullPath)!, cueSheet.SourceFileName));
                var sourceRelativePath = NormalizeRelativePath(Path.GetRelativePath(mediaRoot, sourceFullPath));

                if (!string.Equals(Path.GetExtension(sourceFullPath), ".flac", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Skipping cue sheet {Path}: only single-image FLAC cues are supported.", cueFile.RelativePath);
                    continue;
                }

                if (!File.Exists(sourceFullPath))
                {
                    logger.LogWarning("Skipping cue sheet {Path}: referenced FLAC image not found at {SourcePath}.", cueFile.RelativePath, sourceRelativePath);
                    continue;
                }

                if (!claimedAudioPaths.Add(sourceRelativePath))
                {
                    logger.LogWarning("Skipping cue sheet {Path}: source FLAC {SourcePath} is already claimed by another cue sheet.", cueFile.RelativePath, sourceRelativePath);
                    continue;
                }

                AudioMetadata sourceMetadata;
                try
                {
                    sourceMetadata = metadataReader.Read(sourceFullPath);
                }
                catch (Exception ex)
                {
                    claimedAudioPaths.Remove(sourceRelativePath);
                    logger.LogWarning(ex, "Skipping cue sheet {Path}: failed to read metadata from source FLAC {SourcePath}.", cueFile.RelativePath, sourceRelativePath);
                    continue;
                }

                if (sourceMetadata.DurationMs is not > 0)
                {
                    claimedAudioPaths.Remove(sourceRelativePath);
                    logger.LogWarning("Skipping cue sheet {Path}: source FLAC {SourcePath} does not expose a usable duration.", cueFile.RelativePath, sourceRelativePath);
                    continue;
                }

                var cueImports = BuildCueTrackImports(cueFile, sourceRelativePath, cueSheet, sourceMetadata);
                if (cueImports.Count == 0)
                {
                    claimedAudioPaths.Remove(sourceRelativePath);
                    logger.LogWarning("Skipping cue sheet {Path}: no playable cue tracks were materialized.", cueFile.RelativePath);
                    continue;
                }

                result.AddRange(cueImports);
            }
            catch (CueSheetUnsupportedException ex)
            {
                logger.LogInformation("Skipping cue sheet {Path}: {Reason}", cueFile.RelativePath, ex.Message);
            }
            catch (Exception ex)
            {
                errorCount++;
                logger.LogWarning(ex, "Failed to parse cue sheet {Path}. Continuing library scan.", cueFile.RelativePath);
            }
        }

        return result;
    }

    private List<CueTrackImport> BuildCueTrackImports(
        ScannedFile cueFile,
        string sourceRelativePath,
        CueSheetDocument cueSheet,
        AudioMetadata sourceMetadata)
    {
        var albumTitle = NormalizeOptional(cueSheet.Title)
            ?? NormalizeOptional(sourceMetadata.Album)
            ?? Path.GetFileNameWithoutExtension(cueFile.FileName);
        var albumArtist = NormalizeOptional(cueSheet.Performer)
            ?? NormalizeOptional(sourceMetadata.AlbumArtist)
            ?? NormalizeOptional(sourceMetadata.Artist);

        var cueTrackImports = new List<CueTrackImport>(cueSheet.Tracks.Count);
        var sourceLastModifiedUtc = File.GetLastWriteTimeUtc(mediaPathResolver.ResolveMediaFilePath(sourceRelativePath));
        var lastModifiedUtc = new[] { cueFile.LastModifiedUtc, sourceLastModifiedUtc }.Max();

        for (var index = 0; index < cueSheet.Tracks.Count; index++)
        {
            var cueTrack = cueSheet.Tracks[index];
            var startMs = cueTrack.Index01.ToMilliseconds();
            var nextStartMs = index + 1 < cueSheet.Tracks.Count
                ? cueSheet.Tracks[index + 1].Index01.ToMilliseconds()
                : sourceMetadata.DurationMs!.Value;
            var durationMs = nextStartMs - startMs;
            if (durationMs <= 0)
            {
                throw new InvalidOperationException($"Cue track {cueTrack.Number:00} in {cueFile.RelativePath} has a non-positive duration.");
            }

            var title = NormalizeOptional(cueTrack.Title) ?? $"Track {cueTrack.Number:00}";
            var artistName = NormalizeOptional(cueTrack.Performer)
                ?? NormalizeOptional(cueSheet.Performer)
                ?? NormalizeOptional(sourceMetadata.Artist);
            var relativePath = $"{cueFile.RelativePath}#track-{cueTrack.Number:00}";
            cueTrackImports.Add(new CueTrackImport(
                relativePath,
                cueFile.DirectoryRelativePath,
                BuildCueTrackFileName(cueTrack.Number, title),
                cueFile.RelativePath,
                sourceRelativePath,
                title,
                artistName,
                cueTrack.Number,
                albumArtist,
                albumTitle,
                startMs,
                durationMs,
                lastModifiedUtc));
        }

        return cueTrackImports;
    }

    private void ApplyCueTrack(
        TrackEntity trackEntity,
        CueTrackImport cueTrack,
        FolderEntity? folder,
        IDictionary<string, ArtistEntity> artistsByKey,
        IDictionary<string, AlbumEntity> albumsByKey)
    {
        trackEntity.SourceKind = TrackSourceKind.CueSheet;
        trackEntity.SourceRelativePath = cueTrack.SourceRelativePath;
        trackEntity.CueSheetRelativePath = cueTrack.CueSheetRelativePath;
        trackEntity.CueSegmentStartMs = cueTrack.StartMs;
        trackEntity.CueSegmentDurationMs = cueTrack.DurationMs;
        trackEntity.Folder = folder;
        trackEntity.FileName = cueTrack.FileName;
        trackEntity.Title = cueTrack.Title;
        trackEntity.TrackArtistName = cueTrack.TrackArtistName;
        trackEntity.DiscNumber = 1;
        trackEntity.TrackNumber = cueTrack.TrackNumber;
        trackEntity.Format = LibraryAudioFormats.Wav.Format;
        trackEntity.MimeType = LibraryAudioFormats.Wav.MimeType;
        trackEntity.FileSize = 0;
        trackEntity.LastModifiedUtc = cueTrack.LastModifiedUtc;
        trackEntity.DurationMs = cueTrack.DurationMs;

        if (!string.IsNullOrWhiteSpace(cueTrack.AlbumArtistName) && !string.IsNullOrWhiteSpace(cueTrack.AlbumTitle))
        {
            AssignAlbum(
                trackEntity,
                cueTrack.AlbumArtistName,
                cueTrack.AlbumTitle,
                cueTrack.DirectoryRelativePath,
                artistsByKey,
                albumsByKey);
        }
        else
        {
            trackEntity.Album = null;
            trackEntity.AlbumId = null;
        }
    }

    private TrackEntity CreateTrackEntity(string relativePath, IDictionary<string, TrackEntity> tracksByPath)
    {
        var trackEntity = new TrackEntity
        {
            RelativePath = relativePath
        };
        dbContext.Tracks.Add(trackEntity);
        tracksByPath.Add(relativePath, trackEntity);
        return trackEntity;
    }

    private void AssignAlbum(
        TrackEntity trackEntity,
        string albumArtistName,
        string albumTitle,
        string albumPathKey,
        IDictionary<string, ArtistEntity> artistsByKey,
        IDictionary<string, AlbumEntity> albumsByKey)
    {
        var artist = ResolveArtist(albumArtistName, artistsByKey);
        trackEntity.Album = ResolveAlbum(artist, albumTitle, albumPathKey, albumsByKey);
    }

    private static ScannedFile CreateScannedFile(string mediaRoot, string fullPath)
    {
        var relativePath = NormalizeRelativePath(Path.GetRelativePath(mediaRoot, fullPath));
        var directoryRelativePath = NormalizeRelativePath(Path.GetDirectoryName(relativePath) ?? string.Empty);
        return new ScannedFile(fullPath, relativePath, directoryRelativePath, Path.GetFileName(fullPath), File.GetLastWriteTimeUtc(fullPath));
    }

    private static bool IsHiddenFile(string path) =>
        Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal);

    private static HashSet<string> CollectFolderPaths(IEnumerable<string> relativeDirectoryPaths)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relativeDirectoryPath in relativeDirectoryPaths)
        {
            foreach (var folderPath in EnumerateAncestorFolders(relativeDirectoryPath))
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

    private static string BuildCueTrackFileName(int trackNumber, string title)
    {
        var sanitizedTitle = string.Concat(title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return $"{trackNumber:00}-{sanitizedTitle}.wav";
    }

    private sealed record ScannedFile(
        string FullPath,
        string RelativePath,
        string DirectoryRelativePath,
        string FileName,
        DateTime LastModifiedUtc);

    private sealed record CueTrackImport(
        string RelativePath,
        string DirectoryRelativePath,
        string FileName,
        string CueSheetRelativePath,
        string SourceRelativePath,
        string Title,
        string? TrackArtistName,
        int TrackNumber,
        string? AlbumArtistName,
        string? AlbumTitle,
        long StartMs,
        long DurationMs,
        DateTime LastModifiedUtc);
}
