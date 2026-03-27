using System.Xml.Linq;
using Phylet.Data.Library;
using Phylet.Services;
using Xunit;

namespace Phylet.Tests;

public sealed class DidlBuilderTests
{
    private const string RootId = "0";
    private const string ArtistsRootId = "artists";
    private const string AlbumsRootId = "albums";
    private const string FilesRootId = "files";

    [Fact]
    public void BuildBrowse_AddsSectionIconsToRootContainers()
    {
        var builder = new DidlBuilder();
        var result = builder.BuildBrowse(
            new LibraryBrowseResult(
                LibraryBrowseStatus.Success,
                [
                    new LibraryContainerEntry(ArtistsRootId, RootId, "Artists", LibraryPresentation.ArtistContainerClass, 1),
                    new LibraryContainerEntry(AlbumsRootId, RootId, "Albums", LibraryPresentation.AlbumContainerClass, 1),
                    new LibraryContainerEntry(FilesRootId, RootId, "Files", LibraryPresentation.FolderContainerClass, 1)
                ],
                3,
                1),
            new Uri("http://127.0.0.1:5128"));

        var didl = XDocument.Parse(result.ResultXml);
        var albumArtUrls = didl.Root!
            .Elements()
            .Select(element => element.Elements().First(child => child.Name.LocalName == "albumArtURI"))
            .Select(element => element.Value)
            .ToArray();

        Assert.Equal(
            [
                "http://127.0.0.1:5128/icons/artists-120.png",
                "http://127.0.0.1:5128/icons/albums-120.png",
                "http://127.0.0.1:5128/icons/files-120.png"
            ],
            albumArtUrls);

        Assert.All(
            didl.Root!.Elements().Select(element => element.Elements().First(child => child.Name.LocalName == "albumArtURI")),
            element => Assert.Equal(
                LibraryPresentation.PngAlbumArtProfileId,
                element.Attributes().First(attribute => attribute.Name.LocalName == "profileID").Value));
    }
}
