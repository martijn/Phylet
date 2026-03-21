namespace Phylet.Data.Library;

public static class LibraryAudioFormats
{
    private const string DlnaFlags = "01700000000000000000000000000000";
    private const string GenericDlnaContentFeatures = $"DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS={DlnaFlags}";

    public static readonly LibraryAudioFormat Mp3 = new(
        Extension: ".mp3",
        Format: "mp3",
        MimeType: "audio/mpeg",
        DlnaContentFeatures: $"DLNA.ORG_PN=MP3;{GenericDlnaContentFeatures}",
        ProtocolInfoFeatures: $"DLNA.ORG_PN=MP3;{GenericDlnaContentFeatures}");

    public static readonly LibraryAudioFormat Flac = new(
        Extension: ".flac",
        Format: "flac",
        MimeType: "audio/flac",
        DlnaContentFeatures: GenericDlnaContentFeatures,
        ProtocolInfoFeatures: GenericDlnaContentFeatures);

    public static readonly LibraryAudioFormat M4a = new(
        Extension: ".m4a",
        Format: "m4a",
        MimeType: "audio/mp4",
        DlnaContentFeatures: GenericDlnaContentFeatures,
        ProtocolInfoFeatures: GenericDlnaContentFeatures);

    public static readonly LibraryAudioFormat Ogg = new(
        Extension: ".ogg",
        Format: "ogg",
        MimeType: "audio/ogg",
        DlnaContentFeatures: GenericDlnaContentFeatures,
        ProtocolInfoFeatures: GenericDlnaContentFeatures);

    public static readonly LibraryAudioFormat Wav = new(
        Extension: ".wav",
        Format: "wav",
        MimeType: "audio/wav",
        DlnaContentFeatures: GenericDlnaContentFeatures,
        ProtocolInfoFeatures: GenericDlnaContentFeatures);

    public static readonly LibraryAudioFormat Aiff = new(
        Extension: ".aiff",
        Format: "aiff",
        MimeType: "audio/aiff",
        DlnaContentFeatures: GenericDlnaContentFeatures,
        ProtocolInfoFeatures: GenericDlnaContentFeatures);

    public static readonly LibraryAudioFormat Aif = new(
        Extension: ".aif",
        Format: "aiff",
        MimeType: "audio/aiff",
        DlnaContentFeatures: GenericDlnaContentFeatures,
        ProtocolInfoFeatures: GenericDlnaContentFeatures);

    private static readonly IReadOnlyDictionary<string, LibraryAudioFormat> FormatsByExtension =
        new Dictionary<string, LibraryAudioFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [Mp3.Extension] = Mp3,
            [Flac.Extension] = Flac,
            [M4a.Extension] = M4a,
            [Ogg.Extension] = Ogg,
            [Wav.Extension] = Wav,
            [Aiff.Extension] = Aiff,
            [Aif.Extension] = Aif
        };

    public static IReadOnlyList<LibraryAudioFormat> All { get; } =
    [
        Mp3,
        Flac,
        M4a,
        Ogg,
        Wav,
        Aiff
    ];

    public static bool TryGetByExtension(string extension, out LibraryAudioFormat format) =>
        FormatsByExtension.TryGetValue(extension, out format!);

    public static LibraryAudioFormat ResolveByMimeType(string mimeType) =>
        All.FirstOrDefault(candidate => string.Equals(candidate.MimeType, mimeType, StringComparison.OrdinalIgnoreCase))
        ?? new LibraryAudioFormat(string.Empty, "unknown", mimeType, GenericDlnaContentFeatures, GenericDlnaContentFeatures);
}
