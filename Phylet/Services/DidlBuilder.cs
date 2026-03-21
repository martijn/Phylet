using System.Xml.Linq;
using Phylet.Data.Library;

namespace Phylet.Services;

public sealed record DidlBrowseResult(string ResultXml, int NumberReturned, int TotalMatches, int UpdateId);

public sealed class DidlBuilder
{
    private static readonly XNamespace DidlNs = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace UpnpNs = "urn:schemas-upnp-org:metadata-1-0/upnp/";
    private static readonly XNamespace DlnaNs = "urn:schemas-dlna-org:metadata-1-0/";

    public DidlBrowseResult BuildBrowse(LibraryBrowseResult browseResult, Uri baseUri)
    {
        var nodes = browseResult.Entries.Select(entry => BuildNode(entry, baseUri)).ToList();

        var didl = new XElement(DidlNs + "DIDL-Lite",
            new XAttribute(XNamespace.Xmlns + "dc", DcNs),
            new XAttribute(XNamespace.Xmlns + "upnp", UpnpNs),
            new XAttribute(XNamespace.Xmlns + "dlna", DlnaNs),
            nodes);

        var xml = didl.ToString(SaveOptions.DisableFormatting);
        return new DidlBrowseResult(xml, nodes.Count, browseResult.TotalMatches, browseResult.UpdateId);
    }

    private static XElement BuildNode(LibraryBrowseEntry entry, Uri baseUri) =>
        entry switch
        {
            LibraryContainerEntry container => BuildContainerNode(container, baseUri),
            LibraryTrackEntry track => BuildTrackNode(track, baseUri),
            _ => throw new InvalidOperationException($"Unsupported browse entry type {entry.GetType().Name}.")
        };

    private static XElement BuildContainer(string id, string parentId, string title, string @class, int childCount) =>
        new(DidlNs + "container",
            new XAttribute("id", id),
            new XAttribute("parentID", parentId),
            new XAttribute("restricted", "1"),
            new XAttribute("childCount", childCount),
            new XElement(DcNs + "title", title),
            new XElement(UpnpNs + "class", @class));

    private static XElement BuildContainerNode(LibraryContainerEntry container, Uri baseUri)
    {
        var node = BuildContainer(container.ObjectId, container.ParentObjectId, container.Title, container.UpnpClass, container.ChildCount);
        if (container.AlbumArtAlbumId.HasValue)
        {
            node.Add(new XElement(
                UpnpNs + "albumArtURI",
                new XAttribute(DlnaNs + "profileID", LibraryPresentation.AlbumArtProfileId),
                BuildCoverUrl(baseUri, container.AlbumArtAlbumId.Value)));
        }

        return node;
    }

    private static XElement BuildTrackNode(LibraryTrackEntry track, Uri baseUri)
    {
        var res = new XElement(
            DidlNs + "res",
            new XAttribute("protocolInfo", $"http-get:*:{track.MimeType}:{LibraryAudioFormats.ResolveByMimeType(track.MimeType).ProtocolInfoFeatures}"),
            BuildTrackUrl(baseUri, track.TrackId));
        if (track.FileSize > 0)
        {
            res.SetAttributeValue("size", track.FileSize);
        }

        var item = new XElement(
            DidlNs + "item",
            new XAttribute("id", track.ObjectId),
            new XAttribute("parentID", track.ParentObjectId),
            new XAttribute("restricted", "1"),
            new XElement(DcNs + "title", FormatTrackTitle(track)),
            new XElement(UpnpNs + "class", track.UpnpClass),
            res);

        if (!string.IsNullOrWhiteSpace(track.ArtistName))
        {
            item.Add(new XElement(UpnpNs + "artist", track.ArtistName));
        }

        if (!string.IsNullOrWhiteSpace(track.AlbumTitle))
        {
            item.Add(new XElement(UpnpNs + "album", track.AlbumTitle));
        }

        if (track.OriginalTrackNumber.HasValue && track.OriginalTrackNumber.Value > 0)
        {
            item.Add(new XElement(UpnpNs + "originalTrackNumber", track.OriginalTrackNumber.Value));
        }

        if (track.AlbumId.HasValue)
        {
            item.Add(new XElement(
                UpnpNs + "albumArtURI",
                new XAttribute(DlnaNs + "profileID", LibraryPresentation.AlbumArtProfileId),
                BuildCoverUrl(baseUri, track.AlbumId.Value)));
        }

        return item;
    }

    private static string FormatTrackTitle(LibraryTrackEntry track) =>
        track.OriginalTrackNumber is > 0
            ? $"{track.OriginalTrackNumber.Value}. {track.Title}"
            : track.Title;

    private static string BuildTrackUrl(Uri baseUri, int trackId) => new Uri(baseUri, $"/media/audio/{trackId}").ToString();

    private static string BuildCoverUrl(Uri baseUri, int albumId) => new Uri(baseUri, $"/media/image/{albumId}").ToString();
}
