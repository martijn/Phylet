namespace Phylet.Data.Library;

public interface IAudioMetadataReader
{
    AudioMetadata Read(string filePath);
}
