namespace Phylet.Data.Library;

public abstract record LibraryBrowseEntry(
    string ObjectId,
    string ParentObjectId,
    string Title,
    string UpnpClass);

public sealed record LibraryContainerEntry(
    string ObjectId,
    string ParentObjectId,
    string Title,
    string UpnpClass,
    int ChildCount,
    int? AlbumArtAlbumId = null,
    string? AlbumArtProfileId = null) : LibraryBrowseEntry(ObjectId, ParentObjectId, Title, UpnpClass);

public sealed record LibraryTrackEntry(
    string ObjectId,
    string ParentObjectId,
    string Title,
    string UpnpClass,
    int TrackId,
    int? AlbumId,
    string MimeType,
    long FileSize,
    string? ArtistName,
    string? AlbumTitle,
    int? OriginalTrackNumber,
    string? AlbumArtProfileId = null) : LibraryBrowseEntry(ObjectId, ParentObjectId, Title, UpnpClass);

public enum LibraryBrowseStatus
{
    Success,
    NoSuchObject,
    NoSuchContainer
}

public sealed record LibraryBrowseResult(
    LibraryBrowseStatus Status,
    IReadOnlyList<LibraryBrowseEntry> Entries,
    int TotalMatches,
    int UpdateId);

public sealed record LibraryTrackResource(
    int TrackId,
    string FilePath,
    string MimeType,
    string DlnaContentFeatures);

public sealed record LibraryImageResource(
    int AlbumId,
    string? FilePath,
    string? EmbeddedArtworkSourceFilePath,
    string MimeType,
    string DlnaContentFeatures,
    string ProfileId)
{
    public bool IsEmbeddedArtwork => !string.IsNullOrWhiteSpace(EmbeddedArtworkSourceFilePath);
}

public sealed record LibraryStatistics(
    int ArtistCount,
    int AlbumCount,
    int TrackCount,
    int FolderCount,
    long TotalAudioBytes,
    DateTime? LastScanUtc);

public sealed record AudioMetadata(
    string? Title,
    string? Artist,
    string? AlbumArtist,
    string? Album,
    int? TrackNumber,
    int? DiscNumber,
    long? DurationMs);

public sealed record EmbeddedArtworkContent(
    string MimeType,
    byte[] Data);
