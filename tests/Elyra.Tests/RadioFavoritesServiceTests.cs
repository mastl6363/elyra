using Elyra.Models;
using Elyra.Services;

namespace Elyra.Tests;

public sealed class RadioFavoritesServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elyra-radio-tests-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_directory, "favorites.json");

    [Fact]
    public void Toggle_PersistsAndRestoresFavorite()
    {
        var service = new RadioFavoritesService(FilePath);
        var station = Station();

        service.Toggle(station);
        var restored = new RadioFavoritesService(FilePath);

        Assert.True(restored.Contains(station.StationUuid));
        Assert.Equal("Test Radio", Assert.Single(restored.Favorites).Name);
    }

    [Fact]
    public void Toggle_RemovesExistingFavorite()
    {
        var service = new RadioFavoritesService(FilePath);
        var station = Station();
        service.Toggle(station);

        service.Toggle(station);

        Assert.Empty(service.Favorites);
        Assert.Empty(new RadioFavoritesService(FilePath).Favorites);
    }

    private static RadioStation Station() => new()
    {
        StationUuid = "station-1",
        Name = "Test Radio",
        ResolvedUrl = "https://stream.test/radio",
        Country = "Germany"
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
