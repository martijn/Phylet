namespace Phylet.Data.Library;

internal static class LibraryObjectIds
{
    public const string Root = "0";
    public const string ArtistsRoot = "artists";
    public const string AlbumsRoot = "albums";
    public const string FilesRoot = "files";

    private const string ArtistPrefix = "artist:";
    private const string AlbumPrefix = "album:";
    private const string FolderPrefix = "folder:";
    private const string TrackPrefix = "track:";
    private const string FileTrackPrefix = "file-track:";

    public static ParsedLibraryObjectId Parse(string objectId)
    {
        if (string.Equals(objectId, Root, StringComparison.Ordinal))
        {
            return new ParsedLibraryObjectId(LibraryObjectKind.Root, 0);
        }

        if (string.Equals(objectId, ArtistsRoot, StringComparison.Ordinal))
        {
            return new ParsedLibraryObjectId(LibraryObjectKind.ArtistsRoot, 0);
        }

        if (string.Equals(objectId, AlbumsRoot, StringComparison.Ordinal))
        {
            return new ParsedLibraryObjectId(LibraryObjectKind.AlbumsRoot, 0);
        }

        if (string.Equals(objectId, FilesRoot, StringComparison.Ordinal))
        {
            return new ParsedLibraryObjectId(LibraryObjectKind.FilesRoot, 0);
        }

        return TryParsePrefixedId(objectId, ArtistPrefix, LibraryObjectKind.Artist)
            ?? TryParsePrefixedId(objectId, AlbumPrefix, LibraryObjectKind.Album)
            ?? TryParsePrefixedId(objectId, FolderPrefix, LibraryObjectKind.Folder)
            ?? TryParsePrefixedId(objectId, TrackPrefix, LibraryObjectKind.Track)
            ?? TryParsePrefixedId(objectId, FileTrackPrefix, LibraryObjectKind.FileTrack)
            ?? new ParsedLibraryObjectId(LibraryObjectKind.Invalid, 0);
    }

    public static string Artist(int artistId) => $"{ArtistPrefix}{artistId}";
    public static string Album(int albumId) => $"{AlbumPrefix}{albumId}";
    public static string Folder(int folderId) => $"{FolderPrefix}{folderId}";
    public static string Track(int trackId) => $"{TrackPrefix}{trackId}";
    public static string FileTrack(int trackId) => $"{FileTrackPrefix}{trackId}";

    private static ParsedLibraryObjectId? TryParsePrefixedId(string objectId, string prefix, LibraryObjectKind kind)
    {
        if (!objectId.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(objectId[prefix.Length..], out var entityId))
        {
            return null;
        }

        return new ParsedLibraryObjectId(kind, entityId);
    }
}

internal enum LibraryObjectKind
{
    Invalid,
    Root,
    ArtistsRoot,
    AlbumsRoot,
    FilesRoot,
    Artist,
    Album,
    Folder,
    Track,
    FileTrack
}

internal readonly record struct ParsedLibraryObjectId(LibraryObjectKind Kind, int EntityId);
