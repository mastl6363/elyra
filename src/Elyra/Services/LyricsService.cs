using System.Globalization;
using System.Text.RegularExpressions;
using Elyra.Models;

namespace Elyra.Services;

public sealed partial class LyricsService
{
    public async Task<LyricsDocument?> LoadAsync(Track track, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(track.FilePath))
            return null;

        var sidecarPath = FindSidecar(track.FilePath);
        try
        {
            if (sidecarPath is not null)
            {
                var content = await File.ReadAllTextAsync(sidecarPath, cancellationToken);
                var document = Parse(content, "LRC-Datei");
                if (document is not null)
                    return document;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        try
        {
            var embedded = await Task.Run(() =>
            {
                using var file = TagLib.File.Create(track.FilePath);
                return file.Tag.Lyrics;
            }, cancellationToken);
            return Parse(embedded, "Datei-Tag");
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static string? FindSidecar(string audioPath)
    {
        var directPath = Path.ChangeExtension(audioPath, ".lrc");
        if (File.Exists(directPath))
            return directPath;

        try
        {
            var directory = Path.GetDirectoryName(audioPath);
            var baseName = Path.GetFileNameWithoutExtension(audioPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(baseName))
                return null;
            return Directory.EnumerateFiles(directory, $"{baseName}.*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".lrc", StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    public LyricsDocument? Parse(string? content, string source = "Songtext")
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var synchronized = new List<LyricsLine>();
        var plain = new List<LyricsLine>();
        var offset = 0;
        var offsetMatch = OffsetPattern().Match(content);
        if (offsetMatch.Success)
            _ = int.TryParse(offsetMatch.Groups["milliseconds"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);

        foreach (var rawLine in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var matches = TimestampPattern().Matches(rawLine);
            var text = TimestampPattern().Replace(rawLine, "").Trim();
            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    if (TryGetTimestamp(match, out var timestamp))
                        synchronized.Add(new LyricsLine(
                            timestamp + TimeSpan.FromMilliseconds(offset) < TimeSpan.Zero
                                ? TimeSpan.Zero
                                : timestamp + TimeSpan.FromMilliseconds(offset),
                            text));
                }
                continue;
            }

            if (!MetadataPattern().IsMatch(rawLine) && !string.IsNullOrWhiteSpace(rawLine))
                plain.Add(new LyricsLine(null, rawLine.Trim()));
        }

        if (synchronized.Count > 0)
        {
            return new LyricsDocument(
                source,
                synchronized.OrderBy(line => line.Timestamp).ToList());
        }

        return plain.Count == 0 ? null : new LyricsDocument(source, plain);
    }

    private static bool TryGetTimestamp(Match match, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        if (!int.TryParse(match.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(match.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds > 59)
            return false;

        var fractionText = match.Groups["fraction"].Value;
        var milliseconds = fractionText.Length switch
        {
            1 => int.Parse(fractionText, CultureInfo.InvariantCulture) * 100,
            2 => int.Parse(fractionText, CultureInfo.InvariantCulture) * 10,
            3 => int.Parse(fractionText, CultureInfo.InvariantCulture),
            _ => 0
        };
        timestamp = TimeSpan.FromMinutes(minutes)
            + TimeSpan.FromSeconds(seconds)
            + TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    [GeneratedRegex(@"\[(?<minutes>\d{1,3}):(?<seconds>\d{2})(?:[\.:](?<fraction>\d{1,3}))?\]", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"^\s*\[(ar|al|ti|au|by|offset|re|ve):.*\]\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetadataPattern();

    [GeneratedRegex(@"\[offset:(?<milliseconds>[+-]?\d+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OffsetPattern();
}
