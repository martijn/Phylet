namespace Phylet.Data.Library;

public sealed class ArtistEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;

    public ICollection<AlbumEntity> Albums { get; set; } = [];
}
