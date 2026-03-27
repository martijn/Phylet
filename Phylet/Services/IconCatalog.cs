namespace Phylet.Services;

public static class IconCatalog
{
    private const string ArtistsRootId = "artists";
    private const string AlbumsRootId = "albums";
    private const string FilesRootId = "files";

    public static IReadOnlyList<DeviceDescriptionIcon> DeviceDescriptionIcons { get; } =
    [
        new("image/png", 48, 48, 32, "/icons/server-48.png"),
        new("image/png", 120, 120, 32, "/icons/server-120.png"),
        new("image/png", 240, 240, 32, "/icons/server-240.png")
    ];

    public static bool TryGetRootContainerIcon(string objectId, out RootContainerIcon icon)
    {
        icon = objectId switch
        {
            ArtistsRootId => new RootContainerIcon("/icons/artists-120.png", "PNG_TN"),
            AlbumsRootId => new RootContainerIcon("/icons/albums-120.png", "PNG_TN"),
            FilesRootId => new RootContainerIcon("/icons/files-120.png", "PNG_TN"),
            _ => default
        };

        return !string.IsNullOrWhiteSpace(icon.Path);
    }
}

public sealed record DeviceDescriptionIcon(
    string MimeType,
    int Width,
    int Height,
    int Depth,
    string Url);

public readonly record struct RootContainerIcon(
    string Path,
    string ProfileId);
