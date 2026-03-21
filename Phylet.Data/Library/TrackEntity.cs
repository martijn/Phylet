namespace Phylet.Data.Library;

public sealed class TrackEntity
{
    public int Id { get; set; }
    public int? AlbumId { get; set; }
    public AlbumEntity? Album { get; set; }
    public int? FolderId { get; set; }
    public FolderEntity? Folder { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? TrackArtistName { get; set; }
    public int DiscNumber { get; set; }
    public int TrackNumber { get; set; }
    public string Format { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public long? DurationMs { get; set; }
}
