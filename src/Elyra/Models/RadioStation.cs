using System.Text.Json.Serialization;

namespace Elyra.Models;

/// <summary>A freely available internet-radio station returned by Radio Browser.</summary>
public sealed class RadioStation
{
    [JsonPropertyName("stationuuid")]
    public string StationUuid { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("url_resolved")]
    public string ResolvedUrl { get; set; } = "";

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = "";

    [JsonPropertyName("favicon")]
    public string Favicon { get; set; } = "";

    [JsonPropertyName("tags")]
    public string Tags { get; set; } = "";

    [JsonPropertyName("country")]
    public string Country { get; set; } = "";

    [JsonPropertyName("countrycode")]
    public string CountryCode { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = "";

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; set; }

    [JsonPropertyName("votes")]
    public int Votes { get; set; }

    [JsonIgnore]
    public string StreamUrl => IsHttpUrl(ResolvedUrl) ? ResolvedUrl : Url;

    [JsonIgnore]
    public string Details => string.Join(" · ", new[]
    {
        Country,
        Codec,
        Bitrate > 0 ? $"{Bitrate} kbit/s" : ""
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    [JsonIgnore]
    public IReadOnlyList<string> TagList => Tags
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Take(4)
        .ToList();

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
