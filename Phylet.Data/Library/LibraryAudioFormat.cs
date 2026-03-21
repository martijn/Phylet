namespace Phylet.Data.Library;

public sealed record LibraryAudioFormat(
    string Extension,
    string Format,
    string MimeType,
    string DlnaContentFeatures,
    string ProtocolInfoFeatures);
