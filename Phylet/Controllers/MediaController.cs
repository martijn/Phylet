using Microsoft.AspNetCore.Mvc;
using Phylet.Data.Library;
using Phylet.Services;

namespace Phylet.Controllers;

[ApiController]
public sealed class MediaController(
    LibraryService library,
    IAudioDecoder audioDecoder,
    IAudioMetadataReader metadataReader,
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

        var path = track.SourceFilePath;
        if (!System.IO.File.Exists(path))
        {
            logger.LogWarning("Audio request file not found for track id {TrackId}. Expected path: {Path}", trackId, path);
            return NotFound();
        }

        Response.Headers["transferMode.dlna.org"] = "Streaming";
        Response.Headers["contentFeatures.dlna.org"] = track.DlnaContentFeatures;

        if (track.SourceKind is TrackSourceKind.CueSheet)
        {
            logger.LogInformation(
                "Serving cue audio track {TrackId} from {Path}, mime={MimeType}, startMs={StartMs}, durationMs={DurationMs}, range={RangeHeader}",
                trackId,
                path,
                track.MimeType,
                track.CueSegmentStartMs,
                track.CueSegmentDurationMs,
                Request.Headers.Range.ToString());

            try
            {
                var generatedStream = audioDecoder.OpenCueTrackStream(
                    path,
                    track.CueSegmentStartMs ?? 0,
                    track.CueSegmentDurationMs);
                return File(generatedStream, track.MimeType, enableRangeProcessing: false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cue audio generation failed for track id {TrackId} from {Path}", trackId, path);
                return NotFound();
            }
        }

        var fileInfo = new FileInfo(path);
        logger.LogInformation(
            "Serving audio track {TrackId} from {Path}, mime={MimeType}, bytes={Length}, range={RangeHeader}",
            trackId,
            path,
            track.MimeType,
            fileInfo.Length,
            Request.Headers.Range.ToString());

        var stream = System.IO.File.OpenRead(path);
        return File(stream, track.MimeType, enableRangeProcessing: track.SupportsRangeProcessing);
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

        if (image.IsEmbeddedArtwork)
        {
            var sourcePath = image.EmbeddedArtworkSourceFilePath!;
            if (!System.IO.File.Exists(sourcePath))
            {
                logger.LogWarning("Embedded artwork source file not found for album id {AlbumId}. Expected path: {Path}", albumId, sourcePath);
                return NotFound();
            }

            EmbeddedArtworkContent? embeddedArtwork;
            try
            {
                embeddedArtwork = metadataReader.ReadEmbeddedArtwork(sourcePath, LibraryPresentation.MaxEmbeddedArtworkBytes);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Embedded artwork extraction failed for album id {AlbumId} from {Path}", albumId, sourcePath);
                return NotFound();
            }

            if (embeddedArtwork is null)
            {
                logger.LogWarning("Embedded artwork no longer available for album id {AlbumId} from {Path}", albumId, sourcePath);
                return NotFound();
            }

            logger.LogInformation(
                "Serving embedded album art {AlbumId} from {Path}, mime={MimeType}, bytes={Length}, range={RangeHeader}",
                albumId,
                sourcePath,
                embeddedArtwork.MimeType,
                embeddedArtwork.Data.Length,
                Request.Headers.Range.ToString());

            Response.Headers["transferMode.dlna.org"] = "Interactive";
            Response.Headers["contentFeatures.dlna.org"] = image.DlnaContentFeatures;
            return File(embeddedArtwork.Data, embeddedArtwork.MimeType, enableRangeProcessing: true);
        }

        var path = image.FilePath!;
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

        var stream = System.IO.File.OpenRead(path);
        return File(stream, image.MimeType, enableRangeProcessing: true);
    }
}
