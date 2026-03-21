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

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
