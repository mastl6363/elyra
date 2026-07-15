using System.Net;
using System.Text;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class RadioBrowserServiceTests
{
    [Fact]
    public async Task SearchAsync_SearchesNameAndGenreAndMergesDuplicates()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Query.Contains("name=")
            ? StationsJson(("one", "Jazz One", "https://stream.test/one"), ("bad", "Broken", "file:///bad"))
            : StationsJson(("one", "Jazz One", "https://stream.test/one"), ("two", "Jazz Two", "http://stream.test/two")));
        var service = CreateService(handler);

        var result = await service.SearchAsync("Jazz", "DE");

        Assert.Equal(2, result.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(handler.Requests, uri => uri.Query.Contains("name=Jazz") && uri.Query.Contains("countrycode=DE"));
        Assert.Contains(handler.Requests, uri => uri.Query.Contains("tag=Jazz") && uri.Query.Contains("hidebroken=true"));
    }

    [Fact]
    public async Task SearchAsync_FallsBackToSecondMirror()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Host == "first.test"
            ? null
            : StationsJson(("one", "Station", "https://stream.test/radio")));
        var service = CreateService(handler);

        var result = await service.SearchAsync(null, null);

        Assert.Single(result);
        Assert.Equal(["first.test", "second.test"], handler.Requests.Select(uri => uri.Host));
    }

    private static RadioBrowserService CreateService(RecordingHandler handler) => new(
        new HttpClient(handler),
        [new Uri("https://first.test/"), new Uri("https://second.test/")]);

    private static string StationsJson(params (string Id, string Name, string Url)[] stations) =>
        "[" + string.Join(',', stations.Select(station => $$"""
        {"stationuuid":"{{station.Id}}","name":"{{station.Name}}","url":"{{station.Url}}","url_resolved":"{{station.Url}}","country":"Germany","countrycode":"DE","codec":"MP3","bitrate":128,"votes":10}
        """)) + "]";

    private sealed class RecordingHandler(Func<HttpRequestMessage, string?> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var body = response(request);
            return Task.FromResult(new HttpResponseMessage(body is null ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? "", Encoding.UTF8, "application/json")
            });
        }
    }
}
