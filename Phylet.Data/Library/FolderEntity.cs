namespace Phylet.Data.Library;

public sealed class FolderEntity
{
    public int Id { get; set; }
    public int? ParentFolderId { get; set; }
    public FolderEntity? ParentFolder { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public ICollection<FolderEntity> ChildFolders { get; set; } = [];
    public ICollection<TrackEntity> Tracks { get; set; } = [];
}
