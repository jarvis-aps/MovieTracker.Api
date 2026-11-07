using Microsoft.Extensions.Logging.Abstractions;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Tests;

[Collection(PostgresCollection.Name)]
public class MovieServiceTests(PostgresFixture postgres)
{
    // Как в приложении: один контекст на запрос, и настоящий watchlist-сервис работает на том же контексте
    private static MovieService CreateService(TestDatabase db)
    {
        var context = db.NewContext();
        return new MovieService(context, NullLogger<MovieService>.Instance, new WatchlistService(context, NullLogger<WatchlistService>.Instance));
    }

    private static MovieQueryParameters Query(int userId, Status? status = null, SortBy? sortBy = null, bool? sortByDesc = null,
        int? page = null, int? pageSize = null, string? searchPart = null, bool? inWatchlist = null) =>
        new(userId, status, genreId: null, sortBy, sortByDesc, page, pageSize, searchPart, inWatchlist);

    // ---------- валидация ----------

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(-1, -5)]
    public async Task GetMoviesAsync_PageOrPageSizeNotPositive_Throws(int page, int pageSize)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();

        await Assert.ThrowsAsync<InvalidPageSizeException>(() =>
            CreateService(db).GetMoviesAsync(Query(userId, page: page, pageSize: pageSize)));
    }

    // ---------- личные данные пользователя в списке ----------

    [Fact]
    public async Task GetMoviesAsync_MovieWithoutUserData_IsNotWatchedWithNullRating_AndMatchesStatusFilter()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var untouched = await db.AddMovieAsync("Без записи");
        var watched = await db.AddMovieAsync("Просмотрен");
        await db.AddUserDataAsync(userId, watched, Status.Watched, rating: 8);

        var all = await CreateService(db).GetMoviesAsync(Query(userId));
        var notWatched = await CreateService(db).GetMoviesAsync(Query(userId, status: Status.NotWatched));
        var onlyWatched = await CreateService(db).GetMoviesAsync(Query(userId, status: Status.Watched));

        var untouchedDto = Assert.Single(all, m => m.Id == untouched);
        Assert.Equal(Status.NotWatched, untouchedDto.status);
        Assert.Null(untouchedDto.rating);
        Assert.Equal([untouched], notWatched.Select(m => m.Id));
        Assert.Equal([watched], onlyWatched.Select(m => m.Id));
    }

    [Fact]
    public async Task GetMoviesAsync_OtherUsersData_DoesNotLeak()
    {
        var db = await postgres.CreateDatabaseAsync();
        var alice = await db.AddUserAsync("alice");
        var bob = await db.AddUserAsync("bob");
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddUserDataAsync(alice, movieId, Status.Watched, rating: 9);

        var bobsView = Assert.Single(await CreateService(db).GetMoviesAsync(Query(bob)));

        Assert.Equal(Status.NotWatched, bobsView.status);
        Assert.Null(bobsView.rating);
    }

    // Заметка в списке (3.10): своя видна, чужая нет, без записи — null. Заметки у обоих пользователей на одном фильме
    // и разные: без условия UserId в подзапросе список отдал бы заметку первой попавшейся записи
    [Fact]
    public async Task GetMoviesAsync_Notes_AreShownPerUser()
    {
        var db = await postgres.CreateDatabaseAsync();
        var alice = await db.AddUserAsync("alice");
        var bob = await db.AddUserAsync("bob");
        var carol = await db.AddUserAsync("carol");
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddUserDataAsync(alice, movieId, Status.Watched, notes: "заметка Алисы");
        await db.AddUserDataAsync(bob, movieId, Status.Watching, notes: "заметка Боба");

        var alicesView = Assert.Single(await CreateService(db).GetMoviesAsync(Query(alice)));
        var bobsView = Assert.Single(await CreateService(db).GetMoviesAsync(Query(bob)));
        var carolsView = Assert.Single(await CreateService(db).GetMoviesAsync(Query(carol)));

        Assert.Equal("заметка Алисы", alicesView.Notes);
        Assert.Equal("заметка Боба", bobsView.Notes);
        Assert.Null(carolsView.Notes);
    }

    // ---------- поиск и watchlist-фильтр ----------

    // ILike — функция Npgsql: на InMemory/SQLite этот тест невозможен, поэтому тесты идут против настоящего Postgres
    [Theory]
    [InlineData("дюн")]
    [InlineData("ДЮН")]
    [InlineData("юна")]
    public async Task GetMoviesAsync_SearchPart_IsCaseInsensitiveAndMatchesCyrillicSubstring(string searchPart)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var dune = await db.AddMovieAsync("Дюна");
        await db.AddMovieAsync("Матрица");

        var result = await CreateService(db).GetMoviesAsync(Query(userId, searchPart: searchPart));

        Assert.Equal([dune], result.Select(m => m.Id));
    }

    [Fact]
    public async Task GetMoviesAsync_InWatchlistFilter_SplitsCatalogPerUser()
    {
        var db = await postgres.CreateDatabaseAsync();
        var alice = await db.AddUserAsync("alice");
        var bob = await db.AddUserAsync("bob");
        var inList = await db.AddMovieAsync("В списке");
        var notInList = await db.AddMovieAsync("Не в списке");
        await db.AddToWatchlistAsync(alice, inList);

        var all = await CreateService(db).GetMoviesAsync(Query(alice));
        var onlyIn = await CreateService(db).GetMoviesAsync(Query(alice, inWatchlist: true));
        var onlyOut = await CreateService(db).GetMoviesAsync(Query(alice, inWatchlist: false));
        var bobsView = await CreateService(db).GetMoviesAsync(Query(bob));

        Assert.Equal(2, all.Count);
        Assert.Equal([inList], onlyIn.Select(m => m.Id));
        Assert.Equal([notInList], onlyOut.Select(m => m.Id));
        Assert.All(bobsView, m => Assert.False(m.InWatchlist));
    }

    // Граница Movies/Watchlist: в таблице WatchlistItems пусто, а подставной reader говорит «фильм в списке».
    // Флаг истинен — значит, MovieService берёт watchlist только через интерфейс
    [Fact]
    public async Task MovieService_TakesWatchlistFromReader_NotFromTable()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var inList = await db.AddMovieAsync("В списке по мнению reader");
        await db.AddMovieAsync("Не в списке");

        MovieService CreateWithFakeReader() =>
            new(db.NewContext(), NullLogger<MovieService>.Instance, new FakeWatchlistReader(inList));

        var all = await CreateWithFakeReader().GetMoviesAsync(Query(userId));
        var onlyIn = await CreateWithFakeReader().GetMoviesAsync(Query(userId, inWatchlist: true));

        Assert.Equal([inList], all.Where(m => m.InWatchlist).Select(m => m.Id));
        Assert.Equal([inList], onlyIn.Select(m => m.Id));
    }

    private class FakeWatchlistReader(params int[] movieIds) : IWatchlistReader
    {
        public Task<HashSet<int>> GetMovieIdsAsync(int userId) => Task.FromResult(movieIds.ToHashSet());
    }

    // ---------- сортировка и пагинация ----------

    // Название, статус и оценка не уникальны. Без запасного ключа Id порядок внутри группы одинаковых значений случаен
    // и страницы пересекаются (исправлено в 3.2). Все фильмы намеренно одинаковые, и их много: на пяти строках
    // Postgres отдаёт равные в физическом порядке, и сломанный код проходил (проверено мутацией)
    [Theory]
    [InlineData(null, null)]
    [InlineData(SortBy.Title, false)]
    [InlineData(SortBy.Title, true)]
    [InlineData(SortBy.Status, false)]
    [InlineData(SortBy.Status, true)]
    [InlineData(SortBy.Rating, false)]
    [InlineData(SortBy.Rating, true)]
    public async Task GetMoviesAsync_Pagination_PagesDoNotOverlapAndCoverWholeCatalog(SortBy? sortBy, bool? desc)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var expected = await db.AddMoviesAsync("Одинаковое название", count: 120);

        var seen = new List<int>();
        for (var page = 1; page <= 12; page++)
        {
            var result = await CreateService(db).GetMoviesAsync(Query(userId, sortBy: sortBy, sortByDesc: desc, page: page, pageSize: 10));
            seen.AddRange(result.Select(m => m.Id));
        }

        Assert.Equal(expected.Order(), seen.Order());
    }

    // Решение 3.2: фильмы без оценки всегда в конце, в обоих направлениях
    [Theory]
    [InlineData(false, new[] { 4f, 9f })]
    [InlineData(true, new[] { 9f, 4f })]
    public async Task GetMoviesAsync_SortByRating_MoviesWithoutRatingAreAlwaysLast(bool desc, float[] expectedRatedOrder)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var noRating = await db.AddMovieAsync("А без оценки");
        var low = await db.AddMovieAsync("Б низкая");
        var high = await db.AddMovieAsync("В высокая");
        await db.AddUserDataAsync(userId, low, Status.Watched, rating: 4);
        await db.AddUserDataAsync(userId, high, Status.Watched, rating: 9);

        var result = await CreateService(db).GetMoviesAsync(Query(userId, sortBy: SortBy.Rating, sortByDesc: desc));

        Assert.Equal(expectedRatedOrder, result.Take(2).Select(m => m.rating!.Value));
        Assert.Equal(noRating, result[2].Id);
        Assert.Null(result[2].rating);
    }
}
