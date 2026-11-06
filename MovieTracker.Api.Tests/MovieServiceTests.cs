using Microsoft.Extensions.Logging.Abstractions;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Tests;

[Collection(PostgresCollection.Name)]
public class MovieServiceTests(PostgresFixture postgres)
{
    private static MovieService CreateService(TestDatabase db) =>
        new(db.NewContext(), NullLogger<MovieService>.Instance);

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

    [Theory]
    [InlineData(-0.5f)]
    [InlineData(10.5f)]
    public async Task UpdateRatingAsync_OutOfRange_ThrowsAndSavesNothing(float rating)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        await Assert.ThrowsAsync<InvalidRatingException>(() => CreateService(db).UpdateRatingAsync(movieId, userId, rating));
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(10f)]
    public async Task UpdateRatingAsync_BoundaryValues_AreAccepted(float rating)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        var result = await CreateService(db).UpdateRatingAsync(movieId, userId, rating);

        Assert.Equal(rating, result?.rating);
    }

    // ---------- личные данные пользователя ----------

    [Fact]
    public async Task UpdateStatusAsync_NoUserDataYet_CreatesSingleRowAndRepeatDoesNotDuplicate()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        var first = await CreateService(db).UpdateStatusAsync(movieId, userId, Status.Watching);
        var second = await CreateService(db).UpdateStatusAsync(movieId, userId, Status.Watched);

        Assert.Equal(Status.Watching, first?.status);
        Assert.Equal(Status.Watched, second?.status);
        Assert.Equal(1, await db.CountUserDataAsync(userId, movieId));
    }

    [Fact]
    public async Task UpdateStatusAsync_UnknownMovieOrUser_ReturnsNull()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        Assert.Null(await CreateService(db).UpdateStatusAsync(9999, userId, Status.Watched));
        Assert.Null(await CreateService(db).UpdateStatusAsync(movieId, 9999, Status.Watched));
    }

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
