using Elyra.Services;
using Elyra.Models;

namespace Elyra.Tests;

public sealed class LyricsServiceTests
{
    private readonly LyricsService _service = new();

    [Fact]
    public void Parse_OrdersSynchronizedLinesAndSupportsMultipleTimestamps()
    {
        var result = _service.Parse("[ar:Artist]\n[offset:-100]\n[00:12.50]Second\n[00:02.1][00:05.010]First", "Test");

        Assert.NotNull(result);
        Assert.True(result.IsSynchronized);
        Assert.Equal(3, result.Lines.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), result.Lines[0].Timestamp);
        Assert.Equal("First", result.Lines[1].Text);
        Assert.Equal(TimeSpan.FromMilliseconds(12400), result.Lines[2].Timestamp);
    }

    [Fact]
    public void Parse_ReturnsPlainLinesAndSkipsLrcMetadata()
    {
        var result = _service.Parse("[ti:Title]\nFirst line\n\nSecond line");

        Assert.NotNull(result);
        Assert.False(result.IsSynchronized);
        Assert.Equal(["First line", "Second line"], result.Lines.Select(line => line.Text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[ar:Artist]")]
    public void Parse_ReturnsNullWithoutDisplayableText(string? value) =>
        Assert.Null(_service.Parse(value));

    [Fact]
    public async Task LoadAsync_PrefersCaseInsensitiveLrcSidecar()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elyra-lyrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var audioPath = Path.Combine(directory, "Song.mp3");
            await File.WriteAllTextAsync(Path.Combine(directory, "Song.LRC"), "[00:01.00]Hello");

            var result = await _service.LoadAsync(new Track
            {
                FilePath = audioPath,
                Title = "Song",
                Artist = "Artist",
                Album = ""
            });

            Assert.NotNull(result);
            Assert.Equal("LRC-Datei", result.Source);
            Assert.Equal("Hello", Assert.Single(result.Lines).Text);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
