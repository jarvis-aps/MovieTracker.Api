using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using MovieTracker.Api.Queries;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Tests;

// Расчёт страницы каталога (фильтры, сортировки, границы page и pageSize, totalCount, личный вид пользователя) через mediator
// на настоящем DI и Postgres. Раньше эти тесты вызывали MovieService.GetMoviesAsync напрямую
[Collection(PostgresCollection.Name)]
public class GetMoviesTests(PostgresFixture postgres)
{
    // Как в приложении: запрос уходит через mediator (с замером), один контекст (scope) на вызов, настоящий watchlist-сервис на том же контексте
    private static async Task<MoviesPage> GetPageAsync(TestDatabase db, GetMoviesQuery query)
    {
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query);
    }

    // page и pageSize, которые тест не передал, остаются теми, что записаны в самом запросе (1 и 100): дефолты здесь не дублируются,
    // иначе тесты «без pageSize — сто фильмов» проверяли бы копию, а не настоящий дефолт
    private static GetMoviesQuery Query(int userId, Status? status = null, SortBy? sortBy = null, bool? sortByDesc = null,
        int? page = null, int? pageSize = null, string? searchPart = null, bool? inWatchlist = null, int? genreId = null)
    {
        var query = new GetMoviesQuery(userId, status, GenreId: genreId, SortBy: sortBy, SortByDesc: sortByDesc, SearchPart: searchPart, InWatchlist: inWatchlist);

        if (page is { } p)
            query = query with { Page = p };

        if (pageSize is { } s)
            query = query with { PageSize = s };

        return query;
    }

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
            GetPageAsync(db, Query(userId, page: page, pageSize: pageSize)));
    }

    // Решение 3.12 (число пересмотрено в 3.13): pageSize больше 100 заменяется на 100 (ошибки нет); не больше 100 остаётся
    // как попросил клиент. Фильмов 150, чтобы лишние строки были видны в ответе
    [Theory]
    [InlineData(5, 5)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(1000, 100)]
    public async Task GetMoviesAsync_PageSize_IsCappedAtHundred(int pageSize, int expectedCount)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        await db.AddMoviesAsync("Одинаковое название", count: 150);

        var result = (await GetPageAsync(db, Query(userId, pageSize: pageSize))).Items;

        Assert.Equal(expectedCount, result.Count);
    }

    // Решение 3.13: без pageSize страница из 100 фильмов (раньше было 10)
    [Fact]
    public async Task GetMoviesAsync_NoPageSize_ReturnsHundredMovies()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        await db.AddMoviesAsync("Одинаковое название", count: 150);

        var result = (await GetPageAsync(db, Query(userId))).Items;

        Assert.Equal(100, result.Count);
    }

    // Смещение считается от урезанного размера: при pageSize=1000 вторая страница — это фильмы 101–150, а не пустой список
    [Fact]
    public async Task GetMoviesAsync_PageSizeAboveMax_SecondPageContinuesAfterHundredthMovie()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var expected = await db.AddMoviesAsync("Одинаковое название", count: 150);

        var first = (await GetPageAsync(db, Query(userId, page: 1, pageSize: 1000))).Items;
        var second = (await GetPageAsync(db, Query(userId, page: 2, pageSize: 1000))).Items;

        Assert.Equal(expected.Order().Take(100), first.Select(m => m.Id));
        Assert.Equal(expected.Order().Skip(100), second.Select(m => m.Id));
    }

    // ---------- общее число (totalCount) и страница за последней, 3.13 ----------

    // Страница за последней — пустой список и настоящий totalCount, не ошибка и не запрос в базу. Смещение (page - 1) * pageSize
    // раньше считалось в int и переполнялось. Значения измерены при pageSize 10 (как здесь): 214748366 давал 500 (смещение
    // обернулось в отрицательное), 429496731 давал чужую страницу (смещение 4294967300 обернулось в 4, и вместо пустого
    // списка отдавался пятый фильм)
    [Theory]
    [InlineData(2)]
    [InlineData(101)]
    [InlineData(214748366)]
    [InlineData(429496731)]
    [InlineData(int.MaxValue)]
    public async Task GetMoviesAsync_PageBeyondLast_ReturnsEmptyItemsAndRealTotal(int page)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        await db.AddMoviesAsync("Одинаковое название", count: 5);

        var result = await GetPageAsync(db, Query(userId, page: page, pageSize: 10));

        Assert.Empty(result.Items);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task GetMoviesAsync_EmptyCatalog_ReturnsEmptyItemsAndZeroTotal()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();

        var result = await GetPageAsync(db, Query(userId));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    // totalCount — весь список под фильтрами, а не размер страницы
    [Fact]
    public async Task GetMoviesAsync_TotalCount_IsWholeCatalogNotPageSize()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        await db.AddMoviesAsync("Одинаковое название", count: 150);

        var small = await GetPageAsync(db, Query(userId, pageSize: 10));
        var defaultSize = await GetPageAsync(db, Query(userId));

        Assert.Equal(10, small.Items.Count);
        Assert.Equal(150, small.TotalCount);
        Assert.Equal(100, defaultSize.Items.Count);
        Assert.Equal(150, defaultSize.TotalCount);
    }

    // totalCount считается под теми же фильтрами и для того же пользователя, что и страница: иначе фронт покажет «из 3»
    // там, где листать нечего
    [Fact]
    public async Task GetMoviesAsync_TotalCount_FollowsFiltersAndUser()
    {
        var db = await postgres.CreateDatabaseAsync();
        var alice = await db.AddUserAsync("alice");
        var bob = await db.AddUserAsync("bob");
        var dune = await db.AddMovieAsync("Дюна");
        await db.AddMovieAsync("Дюна: часть вторая");
        var matrix = await db.AddMovieAsync("Матрица");
        await db.AddUserDataAsync(alice, dune, Status.Watched);
        await db.AddUserDataAsync(alice, matrix, Status.Watched);
        await db.AddToWatchlistAsync(alice, matrix);

        var all = await GetPageAsync(db, Query(alice));
        var watched = await GetPageAsync(db, Query(alice, status: Status.Watched));
        var search = await GetPageAsync(db, Query(alice, searchPart: "дюн"));
        var inWatchlist = await GetPageAsync(db, Query(alice, inWatchlist: true));
        var watchedOnePerPage = await GetPageAsync(db, Query(alice, status: Status.Watched, pageSize: 1));
        var bobWatched = await GetPageAsync(db, Query(bob, status: Status.Watched));

        Assert.Equal(3, all.TotalCount);
        Assert.Equal(2, watched.TotalCount);
        Assert.Equal(2, search.TotalCount);
        Assert.Equal(1, inWatchlist.TotalCount);
        Assert.Single(watchedOnePerPage.Items);
        Assert.Equal(2, watchedOnePerPage.TotalCount);
        Assert.Equal(0, bobWatched.TotalCount);
    }

    // Сценарий из задачи: в каталоге 2000 фильмов, фронт просит предпоследнюю страницу при pageSize 100 (записи 1801–1900)
    [Fact]
    public async Task GetMoviesAsync_TwoThousandMovies_SecondToLastPageHasMovies1801To1900()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var expected = (await db.AddMoviesAsync("Одинаковое название", count: 2000)).Order().ToList();

        var secondToLast = await GetPageAsync(db, Query(userId, page: 19, pageSize: 100));
        var last = await GetPageAsync(db, Query(userId, page: 20, pageSize: 100));
        var beyond = await GetPageAsync(db, Query(userId, page: 21, pageSize: 100));

        Assert.Equal(expected.Skip(1800).Take(100), secondToLast.Items.Select(m => m.Id));
        Assert.Equal(2000, secondToLast.TotalCount);
        Assert.Equal(expected.Skip(1900), last.Items.Select(m => m.Id));
        Assert.Empty(beyond.Items);
        Assert.Equal(2000, beyond.TotalCount);
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

        var all = (await GetPageAsync(db, Query(userId))).Items;
        var notWatched = (await GetPageAsync(db, Query(userId, status: Status.NotWatched))).Items;
        var onlyWatched = (await GetPageAsync(db, Query(userId, status: Status.Watched))).Items;

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

        var bobsView = Assert.Single((await GetPageAsync(db, Query(bob))).Items);

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

        var alicesView = Assert.Single((await GetPageAsync(db, Query(alice))).Items);
        var bobsView = Assert.Single((await GetPageAsync(db, Query(bob))).Items);
        var carolsView = Assert.Single((await GetPageAsync(db, Query(carol))).Items);

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

        var result = (await GetPageAsync(db, Query(userId, searchPart: searchPart))).Items;

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

        var all = (await GetPageAsync(db, Query(alice))).Items;
        var onlyIn = (await GetPageAsync(db, Query(alice, inWatchlist: true))).Items;
        var onlyOut = (await GetPageAsync(db, Query(alice, inWatchlist: false))).Items;
        var bobsView = (await GetPageAsync(db, Query(bob))).Items;

        Assert.Equal(2, all.Count);
        Assert.Equal([inList], onlyIn.Select(m => m.Id));
        Assert.Equal([notInList], onlyOut.Select(m => m.Id));
        Assert.All(bobsView, m => Assert.False(m.InWatchlist));
    }

    // Граница Movies/Watchlist: в таблице WatchlistItems пусто, а подставной reader говорит «фильм в списке».
    // Флаг истинен — значит, handler запроса берёт watchlist только через интерфейс. Handler создаётся напрямую, без mediator:
    // нужно подставить свою реализацию IWatchlistReader
    [Fact]
    public async Task GetMoviesHandler_TakesWatchlistFromReader_NotFromTable()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var inList = await db.AddMovieAsync("В списке по мнению reader");
        await db.AddMovieAsync("Не в списке");

        GetMoviesHandler CreateWithFakeReader() =>
            new(db.NewContext(), NullLogger<GetMoviesHandler>.Instance, new FakeWatchlistReader(inList));

        var all = (await CreateWithFakeReader().Handle(Query(userId))).Items;
        var onlyIn = (await CreateWithFakeReader().Handle(Query(userId, inWatchlist: true))).Items;

        Assert.Equal([inList], all.Where(m => m.InWatchlist).Select(m => m.Id));
        Assert.Equal([inList], onlyIn.Select(m => m.Id));
    }

    private class FakeWatchlistReader(params int[] movieIds) : IWatchlistReader
    {
        public Task<HashSet<int>> GetMovieIdsAsync(int userId) => Task.FromResult(movieIds.ToHashSet());
    }

    // Фильтр по жанру: остаются фильмы, у которых этот жанр есть среди жанров, и totalCount считается под тем же фильтром.
    // Остальные жанры такого фильма в ответе не прячутся. В тестах этот параметр раньше не передавался вообще
    // (мутация «фильтр по жанру убран» выжила), поэтому тест добавлен вместе с переносом логики в handler
    [Fact]
    public async Task GetMoviesAsync_GenreFilter_KeepsOnlyMoviesOfThatGenreAndCountsThem()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var action = await db.AddGenreAsync("Action");
        var comedy = await db.AddGenreAsync("Comedy");
        var onlyAction = await db.AddMovieWithGenresAsync("Только боевик", action);
        var both = await db.AddMovieWithGenresAsync("Боевик и комедия", action, comedy);
        await db.AddMovieWithGenresAsync("Только комедия", comedy);
        await db.AddMovieAsync("Без жанров");

        var actionPage = await GetPageAsync(db, Query(userId, genreId: action));
        var unknownPage = await GetPageAsync(db, Query(userId, genreId: 9999));

        Assert.Equal(new[] { onlyAction, both }.Order(), actionPage.Items.Select(m => m.Id).Order());
        Assert.Equal(2, actionPage.TotalCount);
        Assert.Equal([action, comedy], actionPage.Items.Single(m => m.Id == both).Genres.Select(g => g.Id).Order());
        Assert.Empty(unknownPage.Items);
        Assert.Equal(0, unknownPage.TotalCount);
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
            var result = (await GetPageAsync(db, Query(userId, sortBy: sortBy, sortByDesc: desc, page: page, pageSize: 10))).Items;
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

        var result = (await GetPageAsync(db, Query(userId, sortBy: SortBy.Rating, sortByDesc: desc))).Items;

        Assert.Equal(expectedRatedOrder, result.Take(2).Select(m => m.rating!.Value));
        Assert.Equal(noRating, result[2].Id);
        Assert.Null(result[2].rating);
    }
}
