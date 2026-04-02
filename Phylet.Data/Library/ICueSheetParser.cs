namespace Phylet.Data.Library;

public interface ICueSheetParser
{
    CueSheetDocument Parse(string filePath);
}
