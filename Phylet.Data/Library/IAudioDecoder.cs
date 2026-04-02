namespace Phylet.Data.Library;

public interface IAudioDecoder
{
    AudioDecoderAvailability GetAvailability();
    Stream OpenCueTrackStream(string sourceFilePath, long startMs, long? durationMs);
}
