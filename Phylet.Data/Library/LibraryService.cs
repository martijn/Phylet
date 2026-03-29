using Microsoft.EntityFrameworkCore;
using Phylet.Data.Configuration;

namespace Phylet.Data.Library;

public sealed class LibraryService(
    IDbContextFactory<PhyletDbContext> dbContextFactory,
    MediaPathResolver mediaPathResolver)
{
    public async Task<LibraryBrowseResult> BrowseAsync(
        string objectId,
        string browseFlag,
        int startingIndex,
        int requestedCount,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var parsedObjectId = LibraryObjectIds.Parse(objectId);
        if (parsedObjectId.Kind is LibraryObjectKind.Invalid)
        {
            return new LibraryBrowseResult(LibraryBrowseStatus.NoSuchObject, [], 0, 1);
        }

        if (browseFlag.Equals("BrowseMetadata", StringComparison.OrdinalIgnoreCase))
        {
            var metadataEntry = await BuildMetadataAsync(dbContext, parsedObjectId, cancellationToken);
            return metadataEntry is null
                ? new LibraryBrowseResult(LibraryBrowseStatus.NoSuchObject, [], 0, 1)
                : new LibraryBrowseResult(LibraryBrowseStatus.Success, [metadataEntry], 1, 1);
        }

        if (!browseFlag.Equals("BrowseDirectChildren", StringComparison.OrdinalIgnoreCase))
        {
            return new LibraryBrowseResult(LibraryBrowseStatus.NoSuchObject, [], 0, 1);
        }

        if (parsedObjectId.Kind is LibraryObjectKind.Track or LibraryObjectKind.FileTrack)
        {
            return new LibraryBrowseResult(LibraryBrowseStatus.NoSuchContainer, [], 0, 1);
        }

        var children = await BuildChildrenAsync(dbContext, parsedObjectId, cancellationToken);
        if (children is null)
        {
            return new LibraryBrowseResult(LibraryBrowseStatus.NoSuchObject, [], 0, 1);
        }

        var totalMatches = children.Count;
        var pagedChildren = ApplyPaging(children, startingIndex, requestedCount);
        return new LibraryBrowseResult(LibraryBrowseStatus.Success, pagedChildren, totalMatches, 1);
    }

    public async Task<LibraryTrackResource?> GetTrackResourceAsync(int trackId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var track = await dbContext.Tracks
            .Where(entity => entity.Id == trackId)
            .Select(entity => new { entity.Id, entity.RelativePath, entity.MimeType })
            .SingleOrDefaultAsync(cancellationToken);
        if (track is null)
        {
            return null;
        }

        return new LibraryTrackResource(
            track.Id,
            mediaPathResolver.ResolveMediaFilePath(track.RelativePath),
            track.MimeType,
            LibraryAudioFormats.ResolveByMimeType(track.MimeType).DlnaContentFeatures);
    }

    public async Task<LibraryImageResource?> GetAlbumArtAsync(int albumId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var album = await dbContext.Albums
            .Where(entity => entity.Id == albumId)
            .Select(entity => new
            {
                entity.Id,
                entity.CoverRelativePath,
                entity.EmbeddedCoverRelativePath,
                entity.EmbeddedCoverMimeType
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (album is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(album.CoverRelativePath))
        {
            var filePath = mediaPathResolver.ResolveMediaFilePath(album.CoverRelativePath);
            var imageFormat = LibraryPresentation.ResolveImageFormat(filePath);
            return new LibraryImageResource(
                album.Id,
                filePath,
                null,
                imageFormat.MimeType,
                imageFormat.DlnaContentFeatures,
                imageFormat.ProfileId);
        }

        if (string.IsNullOrWhiteSpace(album.EmbeddedCoverRelativePath) || string.IsNullOrWhiteSpace(album.EmbeddedCoverMimeType))
        {
            return null;
        }

        var embeddedSourcePath = mediaPathResolver.ResolveMediaFilePath(album.EmbeddedCoverRelativePath);
        var embeddedImageFormat = LibraryPresentation.ResolveImageFormatFromMimeType(album.EmbeddedCoverMimeType);
        return new LibraryImageResource(
            album.Id,
            null,
            embeddedSourcePath,
            embeddedImageFormat.MimeType,
            embeddedImageFormat.DlnaContentFeatures,
            embeddedImageFormat.ProfileId);
    }

    public async Task<LibraryStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var totalAudioBytes = await dbContext.Tracks
            .Select(track => (long?)track.FileSize)
            .SumAsync(cancellationToken) ?? 0L;
        var lastScanUtc = await dbContext.LibraryScanStates
            .Where(state => state.Id == 1)
            .Select(state => (DateTime?)state.LastScanUtc)
            .SingleOrDefaultAsync(cancellationToken);

        return new LibraryStatistics(
            await dbContext.Artists.CountAsync(cancellationToken),
            await dbContext.Albums.CountAsync(cancellationToken),
            await dbContext.Tracks.CountAsync(cancellationToken),
            await dbContext.Folders.CountAsync(cancellationToken),
            totalAudioBytes,
            lastScanUtc);
    }

    private async Task<LibraryBrowseEntry?> BuildMetadataAsync(
        PhyletDbContext dbContext,
        ParsedLibraryObjectId objectId,
        CancellationToken cancellationToken)
    {
        return objectId.Kind switch
        {
            LibraryObjectKind.Root => new LibraryContainerEntry(LibraryObjectIds.Root, "-1", "Music", LibraryPresentation.MusicContainerClass, 3),
            LibraryObjectKind.ArtistsRoot => new LibraryContainerEntry(
                LibraryObjectIds.ArtistsRoot,
                LibraryObjectIds.Root,
                "Artists",
                LibraryPresentation.ArtistContainerClass,
                await dbContext.Artists.CountAsync(cancellationToken)),
            LibraryObjectKind.AlbumsRoot => new LibraryContainerEntry(
                LibraryObjectIds.AlbumsRoot,
                LibraryObjectIds.Root,
                "Albums",
                LibraryPresentation.AlbumContainerClass,
                await dbContext.Albums.CountAsync(cancellationToken)),
            LibraryObjectKind.FilesRoot => new LibraryContainerEntry(
                LibraryObjectIds.FilesRoot,
                LibraryObjectIds.Root,
                "Files",
                LibraryPresentation.FolderContainerClass,
                await GetFolderChildCountAsync(dbContext, null, cancellationToken)),
            LibraryObjectKind.Artist => await dbContext.Artists
                .Where(artist => artist.Id == objectId.EntityId)
                .Select(artist => new LibraryContainerEntry(
                    LibraryObjectIds.Artist(artist.Id),
                    LibraryObjectIds.ArtistsRoot,
                    artist.Name,
                    LibraryPresentation.ArtistContainerClass,
                    artist.Albums.Count))
                .SingleOrDefaultAsync(cancellationToken),
            LibraryObjectKind.Album => await dbContext.Albums
                .Where(album => album.Id == objectId.EntityId)
                .Select(album => new LibraryContainerEntry(
                    LibraryObjectIds.Album(album.Id),
                    LibraryObjectIds.Artist(album.ArtistId),
                    album.Title,
                    LibraryPresentation.AlbumContainerClass,
                    album.Tracks.Count,
                    album.CoverRelativePath != null || album.EmbeddedCoverRelativePath != null ? album.Id : null,
                    album.CoverRelativePath != null
                        ? LibraryPresentation.ResolveImageFormat(album.CoverRelativePath).ProfileId
                        : album.EmbeddedCoverMimeType != null
                            ? LibraryPresentation.ResolveImageFormatFromMimeType(album.EmbeddedCoverMimeType).ProfileId
                            : null))
                .SingleOrDefaultAsync(cancellationToken),
            LibraryObjectKind.Folder => await dbContext.Folders
                .Where(folder => folder.Id == objectId.EntityId)
                .Select(folder => new LibraryContainerEntry(
                    LibraryObjectIds.Folder(folder.Id),
                    folder.ParentFolderId.HasValue ? LibraryObjectIds.Folder(folder.ParentFolderId.Value) : LibraryObjectIds.FilesRoot,
                    folder.Name,
                    LibraryPresentation.FolderContainerClass,
                    folder.ChildFolders.Count + folder.Tracks.Count))
                .SingleOrDefaultAsync(cancellationToken),
            LibraryObjectKind.Track => await dbContext.Tracks
                .Where(track => track.Id == objectId.EntityId && track.AlbumId.HasValue)
                .Select(track => new LibraryTrackEntry(
                    LibraryObjectIds.Track(track.Id),
                    LibraryObjectIds.Album(track.AlbumId!.Value),
                    track.Title,
                    LibraryPresentation.TrackItemClass,
                    track.Id,
                    track.AlbumId,
                    track.MimeType,
                    track.FileSize,
                    track.TrackArtistName,
                    track.Album!.Title,
                    track.TrackNumber > 0 ? track.TrackNumber : null,
                    track.Album!.CoverRelativePath != null
                        ? LibraryPresentation.ResolveImageFormat(track.Album.CoverRelativePath).ProfileId
                        : track.Album.EmbeddedCoverMimeType != null
                            ? LibraryPresentation.ResolveImageFormatFromMimeType(track.Album.EmbeddedCoverMimeType).ProfileId
                            : null))
                .SingleOrDefaultAsync(cancellationToken),
            LibraryObjectKind.FileTrack => await dbContext.Tracks
                .Where(track => track.Id == objectId.EntityId)
                .Select(track => new LibraryTrackEntry(
                    LibraryObjectIds.FileTrack(track.Id),
                    track.FolderId.HasValue ? LibraryObjectIds.Folder(track.FolderId.Value) : LibraryObjectIds.FilesRoot,
                    track.Title,
                    LibraryPresentation.TrackItemClass,
                    track.Id,
                    track.AlbumId,
                    track.MimeType,
                    track.FileSize,
                    track.TrackArtistName,
                    track.Album != null ? track.Album.Title : null,
                    track.TrackNumber > 0 ? track.TrackNumber : null,
                    track.Album != null && track.Album.CoverRelativePath != null
                        ? LibraryPresentation.ResolveImageFormat(track.Album.CoverRelativePath).ProfileId
                        : track.Album != null && track.Album.EmbeddedCoverMimeType != null
                            ? LibraryPresentation.ResolveImageFormatFromMimeType(track.Album.EmbeddedCoverMimeType).ProfileId
                            : null))
                .SingleOrDefaultAsync(cancellationToken),
            _ => null
        };
    }

    private async Task<List<LibraryBrowseEntry>?> BuildChildrenAsync(
        PhyletDbContext dbContext,
        ParsedLibraryObjectId objectId,
        CancellationToken cancellationToken)
    {
        return objectId.Kind switch
        {
            LibraryObjectKind.Root =>
            [
                new LibraryContainerEntry(LibraryObjectIds.ArtistsRoot, LibraryObjectIds.Root, "Artists", LibraryPresentation.ArtistContainerClass, await dbContext.Artists.CountAsync(cancellationToken)),
                new LibraryContainerEntry(LibraryObjectIds.AlbumsRoot, LibraryObjectIds.Root, "Albums", LibraryPresentation.AlbumContainerClass, await dbContext.Albums.CountAsync(cancellationToken)),
                new LibraryContainerEntry(LibraryObjectIds.FilesRoot, LibraryObjectIds.Root, "Files", LibraryPresentation.FolderContainerClass, await GetFolderChildCountAsync(dbContext, null, cancellationToken))
            ],
            LibraryObjectKind.ArtistsRoot => await dbContext.Artists
                .OrderBy(artist => artist.NormalizedName)
                .ThenBy(artist => artist.Name)
                .Select(artist => (LibraryBrowseEntry)new LibraryContainerEntry(
                    LibraryObjectIds.Artist(artist.Id),
                    LibraryObjectIds.ArtistsRoot,
                    artist.Name,
                    LibraryPresentation.ArtistContainerClass,
                    artist.Albums.Count))
                .ToListAsync(cancellationToken),
            LibraryObjectKind.Artist => await BuildArtistChildrenAsync(dbContext, objectId.EntityId, cancellationToken),
            LibraryObjectKind.AlbumsRoot => await dbContext.Albums
                .OrderBy(album => album.Artist.NormalizedName)
                .ThenBy(album => album.NormalizedTitle)
                .ThenBy(album => album.Title)
                .ThenBy(album => album.AlbumPathKey)
                .Select(album => (LibraryBrowseEntry)new LibraryContainerEntry(
                    LibraryObjectIds.Album(album.Id),
                    LibraryObjectIds.AlbumsRoot,
                    album.Title,
                    LibraryPresentation.AlbumContainerClass,
                    album.Tracks.Count,
                    album.CoverRelativePath != null || album.EmbeddedCoverRelativePath != null ? album.Id : null,
                    album.CoverRelativePath != null
                        ? LibraryPresentation.ResolveImageFormat(album.CoverRelativePath).ProfileId
                        : album.EmbeddedCoverMimeType != null
                            ? LibraryPresentation.ResolveImageFormatFromMimeType(album.EmbeddedCoverMimeType).ProfileId
                            : null))
                .ToListAsync(cancellationToken),
            LibraryObjectKind.Album => await BuildAlbumChildrenAsync(dbContext, objectId.EntityId, cancellationToken),
            LibraryObjectKind.FilesRoot => await BuildFolderChildrenAsync(dbContext, null, LibraryObjectIds.FilesRoot, cancellationToken),
            LibraryObjectKind.Folder => await BuildFolderChildrenAsync(dbContext, objectId.EntityId, LibraryObjectIds.Folder(objectId.EntityId), cancellationToken),
            _ => null
        };
    }

    private static List<LibraryBrowseEntry> ApplyPaging(IReadOnlyList<LibraryBrowseEntry> allEntries, int startingIndex, int requestedCount)
    {
        if (startingIndex >= allEntries.Count)
        {
            return [];
        }

        var page = allEntries.Skip(startingIndex);
        if (requestedCount > 0)
        {
            page = page.Take(requestedCount);
        }

        return page.ToList();
    }

    private async Task<List<LibraryBrowseEntry>?> BuildArtistChildrenAsync(PhyletDbContext dbContext, int artistId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Artists.AnyAsync(artist => artist.Id == artistId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        return await dbContext.Albums
            .Where(album => album.ArtistId == artistId)
            .OrderBy(album => album.NormalizedTitle)
            .ThenBy(album => album.Title)
            .ThenBy(album => album.AlbumPathKey)
            .Select(album => (LibraryBrowseEntry)new LibraryContainerEntry(
                LibraryObjectIds.Album(album.Id),
                LibraryObjectIds.Artist(artistId),
                album.Title,
                LibraryPresentation.AlbumContainerClass,
                album.Tracks.Count,
                album.CoverRelativePath != null || album.EmbeddedCoverRelativePath != null ? album.Id : null,
                album.CoverRelativePath != null
                    ? LibraryPresentation.ResolveImageFormat(album.CoverRelativePath).ProfileId
                    : album.EmbeddedCoverMimeType != null
                        ? LibraryPresentation.ResolveImageFormatFromMimeType(album.EmbeddedCoverMimeType).ProfileId
                        : null))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<LibraryBrowseEntry>?> BuildAlbumChildrenAsync(PhyletDbContext dbContext, int albumId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Albums.AnyAsync(album => album.Id == albumId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        return await dbContext.Tracks
            .Where(track => track.AlbumId == albumId)
            .OrderBy(track => track.DiscNumber)
            .ThenBy(track => track.TrackNumber)
            .ThenBy(track => track.FileName)
            .Select(track => (LibraryBrowseEntry)new LibraryTrackEntry(
                LibraryObjectIds.Track(track.Id),
                LibraryObjectIds.Album(albumId),
                track.Title,
                LibraryPresentation.TrackItemClass,
                track.Id,
                track.AlbumId,
                track.MimeType,
                track.FileSize,
                track.TrackArtistName,
                track.Album!.Title,
                track.TrackNumber > 0 ? track.TrackNumber : null))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<LibraryBrowseEntry>?> BuildFolderChildrenAsync(
        PhyletDbContext dbContext,
        int? parentFolderId,
        string parentObjectId,
        CancellationToken cancellationToken)
    {
        if (parentFolderId.HasValue)
        {
            var folderExists = await dbContext.Folders.AnyAsync(folder => folder.Id == parentFolderId.Value, cancellationToken);
            if (!folderExists)
            {
                return null;
            }
        }

        var folders = await dbContext.Folders
            .Where(folder => folder.ParentFolderId == parentFolderId)
            .OrderBy(folder => folder.Name)
            .Select(folder => (LibraryBrowseEntry)new LibraryContainerEntry(
                LibraryObjectIds.Folder(folder.Id),
                parentObjectId,
                folder.Name,
                LibraryPresentation.FolderContainerClass,
                folder.ChildFolders.Count + folder.Tracks.Count))
            .ToListAsync(cancellationToken);

        var tracks = await dbContext.Tracks
            .Where(track => track.FolderId == parentFolderId)
            .OrderBy(track => track.DiscNumber == 0 ? 1 : 0)
            .ThenBy(track => track.DiscNumber)
            .ThenBy(track => track.TrackNumber == 0 ? 1 : 0)
            .ThenBy(track => track.TrackNumber)
            .ThenBy(track => track.FileName)
            .Select(track => (LibraryBrowseEntry)new LibraryTrackEntry(
                LibraryObjectIds.FileTrack(track.Id),
                parentObjectId,
                track.Title,
                LibraryPresentation.TrackItemClass,
                track.Id,
                track.AlbumId,
                track.MimeType,
                track.FileSize,
                track.TrackArtistName,
                track.Album != null ? track.Album.Title : null,
                track.TrackNumber > 0 ? track.TrackNumber : null))
            .ToListAsync(cancellationToken);

        return folders.Concat(tracks).ToList();
    }

    private static async Task<int> GetFolderChildCountAsync(PhyletDbContext dbContext, int? folderId, CancellationToken cancellationToken)
    {
        var childFolderCount = await dbContext.Folders.CountAsync(folder => folder.ParentFolderId == folderId, cancellationToken);
        var trackCount = await dbContext.Tracks.CountAsync(track => track.FolderId == folderId, cancellationToken);
        return childFolderCount + trackCount;
    }
}
