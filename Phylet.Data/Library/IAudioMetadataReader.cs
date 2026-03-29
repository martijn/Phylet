namespace Phylet.Data.Library;

public interface IAudioMetadataReader
{
    AudioMetadata Read(string filePath);
    EmbeddedArtworkContent? ReadEmbeddedArtwork(string filePath, int maxArtworkBytes);
}
