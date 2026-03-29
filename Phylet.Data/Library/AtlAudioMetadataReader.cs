using ATL;

namespace Phylet.Data.Library;

public sealed class AtlAudioMetadataReader : IAudioMetadataReader
{
    public AudioMetadata Read(string filePath)
    {
        var track = new Track(filePath);

        return new AudioMetadata(
            Title: EmptyToNull(track.Title),
            Artist: EmptyToNull(track.Artist),
            AlbumArtist: EmptyToNull(track.AlbumArtist),
            Album: EmptyToNull(track.Album),
            TrackNumber: track.TrackNumber > 0 ? track.TrackNumber : null,
            DiscNumber: track.DiscNumber > 0 ? track.DiscNumber : null,
            DurationMs: track.DurationMs > 0 ? (long?)track.DurationMs : null);
    }

    public EmbeddedArtworkContent? ReadEmbeddedArtwork(string filePath, int maxArtworkBytes)
    {
        var track = new Track(filePath);

        var picture = track.EmbeddedPictures
            .Where(candidate => candidate.PictureData is { Length: > 0 } && candidate.PictureData.Length <= maxArtworkBytes)
            .OrderBy(GetPicturePriority)
            .ThenBy(candidate => candidate.Position)
            .FirstOrDefault();
        if (picture?.PictureData is null)
        {
            return null;
        }

        return new EmbeddedArtworkContent(
            NormalizeImageMimeType(picture.MimeType),
            picture.PictureData);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int GetPicturePriority(PictureInfo picture) =>
        picture.PicType switch
        {
            PictureInfo.PIC_TYPE.Front => 0,
            PictureInfo.PIC_TYPE.Generic => 1,
            _ => 2
        };

    private static string NormalizeImageMimeType(string? mimeType) =>
        mimeType?.Equals("image/png", StringComparison.OrdinalIgnoreCase) is true
            ? "image/png"
            : "image/jpeg";
}
