using System.Globalization;

namespace Phylet.Data.Library;

public sealed class CueSheetParser : ICueSheetParser
{
    public CueSheetDocument Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        string? sourceFileName = null;
        string? albumTitle = null;
        string? albumPerformer = null;
        var tracks = new List<CueSheetTrackBuilder>();
        CueSheetTrackBuilder? currentTrack = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
            {
                if (sourceFileName is not null)
                {
                    throw new CueSheetUnsupportedException("Cue sheet references multiple FILE entries.");
                }

                sourceFileName = ParseFileName(line["FILE ".Length..]);
                continue;
            }

            if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase))
            {
                var trackRemainder = line["TRACK ".Length..].Trim();
                var trackParts = trackRemainder.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (trackParts.Length < 2 || !int.TryParse(trackParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var trackNumber))
                {
                    throw new InvalidOperationException($"Cue track declaration is invalid: {line}");
                }

                currentTrack = new CueSheetTrackBuilder(trackNumber);
                tracks.Add(currentTrack);
                continue;
            }

            if (line.StartsWith("TITLE ", StringComparison.OrdinalIgnoreCase))
            {
                var value = ParseQuotedValue(line["TITLE ".Length..]);
                if (currentTrack is null)
                {
                    albumTitle = value;
                }
                else
                {
                    currentTrack.Title = value;
                }

                continue;
            }

            if (line.StartsWith("PERFORMER ", StringComparison.OrdinalIgnoreCase))
            {
                var value = ParseQuotedValue(line["PERFORMER ".Length..]);
                if (currentTrack is null)
                {
                    albumPerformer = value;
                }
                else
                {
                    currentTrack.Performer = value;
                }

                continue;
            }

            if (currentTrack is null || !line.StartsWith("INDEX ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var indexRemainder = line["INDEX ".Length..].Trim();
            var indexParts = indexRemainder.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (indexParts.Length != 2 || !int.TryParse(indexParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var indexNumber))
            {
                throw new InvalidOperationException($"Cue index declaration is invalid: {line}");
            }

            var time = ParseTime(indexParts[1]);
            switch (indexNumber)
            {
                case 0:
                    currentTrack.Index00 = time;
                    break;
                case 1:
                    currentTrack.Index01 = time;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(sourceFileName))
        {
            throw new InvalidOperationException("Cue sheet does not contain a FILE entry.");
        }

        var parsedTracks = tracks
            .Where(track => track.Index01 is not null)
            .Select(track => track.Build())
            .OrderBy(track => track.Number)
            .ToArray();
        if (parsedTracks.Length == 0)
        {
            throw new InvalidOperationException("Cue sheet does not contain any playable INDEX 01 tracks.");
        }

        return new CueSheetDocument(sourceFileName, albumTitle, albumPerformer, parsedTracks);
    }

    private static CueSheetTime ParseTime(string value)
    {
        var parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var frames))
        {
            throw new InvalidOperationException($"Cue time is invalid: {value}");
        }

        return new CueSheetTime(minutes, seconds, frames);
    }

    private static string ParseQuotedValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static string ParseFileName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("Cue FILE entry is missing a file name.");
        }

        if (trimmed[0] == '"')
        {
            var closingQuoteIndex = trimmed.IndexOf('"', 1);
            if (closingQuoteIndex < 0)
            {
                throw new InvalidOperationException("Cue FILE entry has an unterminated quoted file name.");
            }

            return trimmed[1..closingQuoteIndex];
        }

        var firstSpaceIndex = trimmed.IndexOf(' ');
        return firstSpaceIndex >= 0 ? trimmed[..firstSpaceIndex] : trimmed;
    }

    private sealed class CueSheetTrackBuilder(int number)
    {
        public int Number { get; } = number;
        public string? Title { get; set; }
        public string? Performer { get; set; }
        public CueSheetTime? Index00 { get; set; }
        public CueSheetTime? Index01 { get; set; }

        public CueSheetTrack Build() => new(
            Number,
            Title,
            Performer,
            Index01 ?? throw new InvalidOperationException("Cue track is missing INDEX 01."),
            Index00);
    }
}
