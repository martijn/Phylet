namespace Phylet.Data.Library;

public sealed class AlbumEntity
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public ArtistEntity Artist { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public string AlbumPathKey { get; set; } = string.Empty;
    public string? CoverRelativePath { get; set; }
    public string? EmbeddedCoverRelativePath { get; set; }
    public string? EmbeddedCoverMimeType { get; set; }

    public ICollection<TrackEntity> Tracks { get; set; } = [];
}
