using Microsoft.AspNetCore.Mvc;
using Phylet.Data.Library;
using Phylet.Services;

namespace Phylet.Controllers;

[ApiController]
public sealed class MediaController(
    LibraryService library,
    ILogger<MediaController> logger) : ControllerBase
{
    [HttpGet("/media/audio/{trackId:int}")]
    [HttpHead("/media/audio/{trackId:int}")]
    public async Task<IActionResult> Audio(int trackId)
    {
        var track = await library.GetTrackResourceAsync(trackId, HttpContext.RequestAborted);
        if (track is null)
        {
            logger.LogWarning("Audio request for unknown track id {TrackId}", trackId);
            return NotFound();
        }

        var path = track.FilePath;
        if (!System.IO.File.Exists(path))
        {
            logger.LogWarning("Audio request file not found for track id {TrackId}. Expected path: {Path}", trackId, path);
            return NotFound();
        }

        var fileInfo = new FileInfo(path);
        logger.LogInformation(
            "Serving audio track {TrackId} from {Path}, mime={MimeType}, bytes={Length}, range={RangeHeader}",
            trackId,
            path,
            track.MimeType,
            fileInfo.Length,
            Request.Headers.Range.ToString());

        Response.Headers["transferMode.dlna.org"] = "Streaming";
        Response.Headers["contentFeatures.dlna.org"] = track.DlnaContentFeatures;

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, track.MimeType, enableRangeProcessing: true);
    }

    [HttpGet("/media/image/{albumId:int}")]
    [HttpHead("/media/image/{albumId:int}")]
    public async Task<IActionResult> Image(int albumId)
    {
        var image = await library.GetAlbumArtAsync(albumId, HttpContext.RequestAborted);
        if (image is null)
        {
            logger.LogDebug("No album art found for album id {AlbumId}", albumId);
            return NotFound();
        }

        var path = image.FilePath;
        if (!System.IO.File.Exists(path))
        {
            logger.LogWarning("Image request file not found for album id {AlbumId}. Expected path: {Path}", albumId, path);
            return NotFound();
        }

        var fileInfo = new FileInfo(path);
        logger.LogInformation(
            "Serving album art {AlbumId} from {Path}, mime={MimeType}, bytes={Length}, range={RangeHeader}",
            albumId,
            path,
            image.MimeType,
            fileInfo.Length,
            Request.Headers.Range.ToString());

        Response.Headers["transferMode.dlna.org"] = "Interactive";
        Response.Headers["contentFeatures.dlna.org"] = image.DlnaContentFeatures;

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, image.MimeType, enableRangeProcessing: true);
    }
}
