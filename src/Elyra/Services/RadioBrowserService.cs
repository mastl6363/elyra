using System.Net.Http.Headers;
using System.Text.Json;
using Elyra.Models;

namespace Elyra.Services;

/// <summary>Search client for the free, community-maintained Radio Browser directory.</summary>
public sealed class RadioBrowserService
{
    private static readonly Uri[] DefaultMirrors =
    [
        new("https://de1.api.radio-browser.info/"),
        new("https://nl1.api.radio-browser.info/")
    ];

    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<Uri> _mirrors;

    public RadioBrowserService() : this(CreateClient(), DefaultMirrors) { }

    public RadioBrowserService(HttpClient httpClient, IReadOnlyList<Uri>? mirrors = null)
    {
        _httpClient = httpClient;
        _mirrors = mirrors is { Count: > 0 } ? mirrors : DefaultMirrors;
    }

    public async Task<IReadOnlyList<RadioStation>> SearchAsync(
        string? query,
        string? countryCode,
        CancellationToken cancellationToken = default)
    {
        query = query?.Trim();
        countryCode = countryCode?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(query))
            return await RequestAsync(BuildSearchPath(null, null, countryCode, 60), cancellationToken);

        // Radio Browser has separate station-name and tag fields. Query both so a
        // single Elyra search finds "Jazz" as a genre and as part of a station name.
        var byName = RequestAsync(BuildSearchPath(query, null, countryCode, 40), cancellationToken);
        var byTag = RequestAsync(BuildSearchPath(null, query, countryCode, 40), cancellationToken);
        var results = await Task.WhenAll(byName, byTag);

        return results
            .SelectMany(stations => stations)
            .GroupBy(station => station.StationUuid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(station => station.Votes)
            .ThenBy(station => station.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(60)
            .ToList();
    }

    public async Task RecordClickAsync(string stationUuid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stationUuid)) return;

        try
        {
            using var response = await SendWithFallbackAsync(
                $"json/url/{Uri.EscapeDataString(stationUuid)}",
                cancellationToken);
        }
        catch
        {
            // Playback must never depend on the optional popularity counter.
        }
    }

    private async Task<IReadOnlyList<RadioStation>> RequestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithFallbackAsync(path, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var stations = await JsonSerializer.DeserializeAsync<List<RadioStation>>(
            stream,
            cancellationToken: cancellationToken) ?? [];

        return stations
            .Where(station => !string.IsNullOrWhiteSpace(station.StationUuid)
                && !string.IsNullOrWhiteSpace(station.Name)
                && RadioStation.IsHttpUrl(station.StreamUrl))
            .ToList();
    }

    private async Task<HttpResponseMessage> SendWithFallbackAsync(
        string path,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var mirror in _mirrors)
        {
            try
            {
                var response = await _httpClient.GetAsync(new Uri(mirror, path), cancellationToken);
                if (response.IsSuccessStatusCode)
                    return response;

                lastError = new HttpRequestException(
                    $"Radio Browser returned {(int)response.StatusCode} from {mirror.Host}.");
                response.Dispose();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        throw new HttpRequestException("Die Radio-Bibliothek ist momentan nicht erreichbar.", lastError);
    }

    private static string BuildSearchPath(string? name, string? tag, string? countryCode, int limit)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("hidebroken", "true"),
            new("order", "votes"),
            new("reverse", "true"),
            new("limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        Add(parameters, "name", name);
        Add(parameters, "tag", tag);
        Add(parameters, "countrycode", countryCode);
        return $"json/stations/search?{string.Join('&', parameters.Select(parameter =>
            $"{parameter.Key}={Uri.EscapeDataString(parameter.Value)}"))}";
    }

    private static void Add(List<KeyValuePair<string, string>> parameters, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parameters.Add(new(key, value));
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Elyra", "1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/mastl6363/elyra)"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
