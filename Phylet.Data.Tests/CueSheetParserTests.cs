using Phylet.Data.Library;
using Xunit;

namespace Phylet.Data.Tests;

public sealed class CueSheetParserTests
{
    [Theory]
    [InlineData("00:60:00")]
    [InlineData("00:00:75")]
    [InlineData("00::00:00")]
    [InlineData(":00:00:00")]
    [InlineData("00:00:00:")]
    [InlineData("-01:00:00")]
    [InlineData("00:-01:00")]
    [InlineData("00:00:-01")]
    [InlineData("2147483648:00:00")]
    [InlineData("00:00")]
    [InlineData("00:aa:00")]
    public void Parse_RejectsInvalidIndexTimes(string timestamp)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ParseTimestamp(timestamp));

        Assert.Equal($"Cue time is invalid: {timestamp}", exception.Message);
    }

    [Theory]
    [InlineData("00:00:00", 0, 0, 0)]
    [InlineData("99:59:74", 99, 59, 74)]
    [InlineData("120:00:00", 120, 0, 0)]
    public void Parse_PreservesValidIndexTimes(string timestamp, int minutes, int seconds, int frames)
    {
        var document = ParseTimestamp(timestamp);

        Assert.Equal(new CueSheetTime(minutes, seconds, frames), Assert.Single(document.Tracks).Index01);
    }

    [Fact]
    public void Parse_RejectsInvalidPregapTime()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ParseTimestamp("00:00:75", "00"));

        Assert.Equal("Cue time is invalid: 00:00:75", exception.Message);
    }

    private static CueSheetDocument ParseTimestamp(string timestamp, string index = "01")
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"FILE \"album.flac\" WAVE\n  TRACK 01 AUDIO\n    INDEX {index} {timestamp}\n");
            return new CueSheetParser().Parse(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
