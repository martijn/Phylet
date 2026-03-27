namespace Phylet.Data.Library;

public static class LibraryPresentation
{
    public const string MusicContainerClass = "object.container";
    public const string ArtistContainerClass = "object.container.person.musicArtist";
    public const string AlbumContainerClass = "object.container.album.musicAlbum";
    public const string FolderContainerClass = "object.container.storageFolder";
    public const string TrackItemClass = "object.item.audioItem.musicTrack";

    public const string JpegAlbumArtProfileId = "JPEG_TN";
    public const string PngAlbumArtProfileId = "PNG_TN";

    private const string JpegDlnaContentFeatures = "DLNA.ORG_PN=JPEG_TN;DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000";
    private const string PngDlnaContentFeatures = "DLNA.ORG_PN=PNG_TN;DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000";

    internal static LibraryImageFormat ResolveImageFormat(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".png" => new LibraryImageFormat("image/png", PngDlnaContentFeatures, PngAlbumArtProfileId),
            _ => new LibraryImageFormat("image/jpeg", JpegDlnaContentFeatures, JpegAlbumArtProfileId)
        };
    }
}

internal sealed record LibraryImageFormat(
    string MimeType,
    string DlnaContentFeatures,
    string ProfileId);
