using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elyra.Models;

namespace Elyra.Services;

/// <summary>
/// Manually enriches missing album metadata through MusicBrainz. It never writes
/// to the source audio files; callers decide whether to persist returned updates.
/// </summary>
public sealed class MusicBrainzMetadataService
{
    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task> _rateLimitDelay;

    public MusicBrainzMetadataService()
        : this(CreateClient(), cancellationToken => Task.Delay(TimeSpan.FromSeconds(1), cancellationToken))
    {
    }

    public MusicBrainzMetadataService(
        HttpClient httpClient,
        Func<CancellationToken, Task>? rateLimitDelay = null)
    {
        _httpClient = httpClient;
        _rateLimitDelay = rateLimitDelay ?? (_ => Task.CompletedTask);
    }

    public async Task<MetadataEnrichmentResult> EnrichMissingAlbumsAsync(
        IEnumerable<Track> tracks,
        IProgress<MetadataEnrichmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var groups = tracks
            .Where(track => string.IsNullOrWhiteSpace(track.Album)
                && !string.IsNullOrWhiteSpace(track.Artist)
                && !string.IsNullOrWhiteSpace(track.Title)
                && track.Duration > TimeSpan.Zero)
            .GroupBy(track => $"{Normalize(track.Artist)}\0{Normalize(track.Title)}\0{(long)track.Duration.TotalMilliseconds / 2000}")
            .Select(group => group.ToList())
            .ToList();

        var updates = new List<TrackMetadataUpdate>();
        var matched = 0;
        var skipped = 0;
        var failed = 0;

        for (var index = 0; index < groups.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = groups[index];
            var track = group[0];

            try
            {
                var match = await FindAlbumAsync(track, cancellationToken);
                if (match is null)
                {
                    skipped += group.Count;
                }
                else
                {
                    updates.AddRange(group.Select(item =>
                        new TrackMetadataUpdate(item.FilePath, match.Album, match.AlbumArtist)));
                    matched += group.Count;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                failed += group.Count;
            }

            progress?.Report(new MetadataEnrichmentProgress(
                index + 1,
                groups.Count,
                matched,
                skipped,
                failed,
                $"{track.Artist} — {track.Title}"));

            if (index + 1 < groups.Count)
                await _rateLimitDelay(cancellationToken);
        }

        return new MetadataEnrichmentResult(updates, matched, skipped, failed);
    }

    private async Task<AlbumMatch?> FindAlbumAsync(Track track, CancellationToken cancellationToken)
    {
        var quantizedDuration = (long)track.Duration.TotalMilliseconds / 2000;
        var query = $"recording:\"{EscapeLucene(track.Title)}\" AND artist:\"{EscapeLucene(track.Artist)}\" AND qdur:{quantizedDuration}";
        var url = $"https://musicbrainz.org/ws/2/recording/?query={Uri.EscapeDataString(query)}&fmt=json&limit=10";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"MusicBrainz returned {(int)response.StatusCode}.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<RecordingSearchResult>(stream, cancellationToken: cancellationToken);
        if (result?.Recordings is null)
            return null;

        var normalizedTitle = Normalize(track.Title);
        var normalizedArtist = Normalize(track.Artist);

        var matches = result.Recordings
            .Where(recording => recording.Score >= 90
                && Normalize(recording.Title) == normalizedTitle
                && recording.Length is not null
                && Math.Abs(recording.Length.Value - track.Duration.TotalMilliseconds) <= 5000
                && recording.ArtistCredit.Any(credit =>
                    Normalize(credit.Name ?? credit.Artist?.Name ?? "") == normalizedArtist))
            .SelectMany(recording => recording.Releases)
            .Where(release => string.Equals(release.Status, "Official", StringComparison.OrdinalIgnoreCase))
            .Where(release => release.ReleaseGroup is not null
                && (string.Equals(release.ReleaseGroup.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(release.ReleaseGroup.PrimaryType, "EP", StringComparison.OrdinalIgnoreCase)))
            .Where(release => release.ReleaseGroup!.SecondaryTypes.All(type =>
                !string.Equals(type, "Compilation", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "DJ-mix", StringComparison.OrdinalIgnoreCase)))
            .Select(release => new AlbumMatch(
                release.ReleaseGroup!.Title ?? release.Title,
                track.Artist,
                release.Date,
                release.ReleaseGroup.Id))
            .Where(match => !string.IsNullOrWhiteSpace(match.Album))
            .GroupBy(match => match.ReleaseGroupId)
            .Select(group => group.OrderBy(match => ReleaseDateSortKey(match.ReleaseDate)).First())
            .OrderBy(match => ReleaseDateSortKey(match.ReleaseDate))
            .ThenBy(match => match.Album, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return matches.FirstOrDefault();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Elyra", "1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/mastl6363/elyra)"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant));
    }

    private static string EscapeLucene(string value)
    {
        const string specialCharacters = "+-!(){}[]^\"~*?:\\/";
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (specialCharacters.Contains(character) || character is '&' or '|')
                builder.Append('\\');
            builder.Append(character);
        }
        return builder.ToString();
    }

    private static int ReleaseDateSortKey(string? date)
    {
        if (date is null)
            return int.MaxValue;

        var yearPart = date.Split('-', 2)[0];
        return int.TryParse(yearPart, out var year) ? year : int.MaxValue;
    }

    private sealed record AlbumMatch(string Album, string AlbumArtist, string? ReleaseDate, string ReleaseGroupId);

    private sealed class RecordingSearchResult
    {
        [JsonPropertyName("recordings")]
        public List<RecordingResult> Recordings { get; set; } = new();
    }

    private sealed class RecordingResult
    {
        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("length")]
        public long? Length { get; set; }

        [JsonPropertyName("artist-credit")]
        public List<ArtistCreditResult> ArtistCredit { get; set; } = new();

        [JsonPropertyName("releases")]
        public List<ReleaseResult> Releases { get; set; } = new();
    }

    private sealed class ArtistCreditResult
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("artist")]
        public ArtistResult? Artist { get; set; }
    }

    private sealed class ArtistResult
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    private sealed class ReleaseResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("release-group")]
        public ReleaseGroupResult? ReleaseGroup { get; set; }
    }

    private sealed class ReleaseGroupResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("primary-type")]
        public string? PrimaryType { get; set; }

        [JsonPropertyName("secondary-types")]
        public List<string> SecondaryTypes { get; set; } = new();
    }
}
