namespace Phylet.Data.Library;

public sealed class LibraryScanState
{
    public int Id { get; set; }
    public DateTime LastScanUtc { get; set; }
    public string? LastError { get; set; }
}
