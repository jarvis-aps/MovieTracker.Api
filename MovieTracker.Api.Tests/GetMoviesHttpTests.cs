using System.Net;
using System.Net.Http.Json;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Tests;

// GET /movies на уровне HTTP: настоящее приложение в памяти (ApiFactory) и настоящий Postgres. Логику фильтров и сортировок проверяют тесты
// GetMoviesQueryTests/GetMoviesTests; здесь только то, что делает сам HTTP-слой: привязка параметров из query-строки, дефолты страницы,
// перевод исключения в 400, форма и имена полей JSON
[Collection(PostgresCollection.Name)]
public class GetMoviesHttpTests(PostgresFixture postgres)
{
    // Первый тест «насквозь»: доказывает, что приложение смотрит в базу теста (данные, положенные напрямую в базу, видны по HTTP)
    // и что JSON имеет ожидаемые имена полей. Статус — число (в Bootstrap.cs нет JsonStringEnumConverter), это часть контракта
    [Fact]
    public async Task Get_ReturnsPersonalPageAsJsonObject()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var genreId = await db.AddGenreAsync("Sci-Fi");
        var movieId = await db.AddMovieWithGenresAsync("Дюна", genreId);
        await db.AddUserDataAsync(userId, movieId, Status.Watched, rating: 8.5f, notes: "Огонь");
        await db.AddToWatchlistAsync(userId, movieId);
        var client = postgres.Api.CreateClient();

        var response = await client.GetAsync($"/movies?userId={userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal(movieId, item.GetProperty("id").GetInt32());
        Assert.Equal("Дюна", item.GetProperty("title").GetString());
        Assert.Equal(2000, item.GetProperty("year").GetInt32());
        Assert.Equal(8.5f, item.GetProperty("rating").GetSingle());
        Assert.Equal((int)Status.Watched, item.GetProperty("status").GetInt32());
        Assert.True(item.GetProperty("inWatchlist").GetBoolean());
        Assert.Equal("Огонь", item.GetProperty("notes").GetString());
        var genre = Assert.Single(item.GetProperty("genres").EnumerateArray());
        Assert.Equal(genreId, genre.GetProperty("id").GetInt32());
        Assert.Equal("Sci-Fi", genre.GetProperty("name").GetString());
    }

    // Дефолты page=1 и pageSize=100 лежат в записи GetMoviesQuery, и подставляет их [AsParameters]: без параметров страницы клиент
    // получает первую страницу из 100, а не 400 и не всё сразу
    [Fact]
    public async Task Get_WithoutPageParameters_ReturnsFirstPageOf100()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        await db.AddMoviesAsync("Фильм", 105);
        var client = postgres.Api.CreateClient();

        var page = await client.GetFromJsonAsync<MoviesPage>($"/movies?userId={userId}");

        Assert.NotNull(page);
        Assert.Equal(100, page.Items.Count);
        Assert.Equal(105, page.TotalCount);
    }

    // Все имена параметров query-строки (searchPart, genreId, sortBy, sortByDesc, page, pageSize) — часть контракта: переименование
    // свойства в записи GetMoviesQuery молча изменило бы API. Данные подобраны так, что без любого из шести параметров ответ другой:
    // нет searchPart или genreId — totalCount 3; нет sortBy — порядок по названию (вторая страница «Часть вторая»);
    // нет sortByDesc — порядок по возрастанию рейтинга (вторая страница «Часть вторая»); нет page — первая страница; нет pageSize — вторая страница пуста
    [Fact]
    public async Task Get_BindsSearchGenreSortingAndPagingFromQueryString()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var scifi = await db.AddGenreAsync("Sci-Fi");
        var comedy = await db.AddGenreAsync("Comedy");
        var dune = await db.AddMovieWithGenresAsync("Дюна", scifi);
        var duneTwo = await db.AddMovieWithGenresAsync("Дюна: Часть вторая", scifi);
        await db.AddMovieWithGenresAsync("Матрица", scifi);
        await db.AddMovieWithGenresAsync("Дюна-комедия", comedy);
        await db.AddUserDataAsync(userId, dune, Status.NotWatched, rating: 5);
        await db.AddUserDataAsync(userId, duneTwo, Status.NotWatched, rating: 9);
        var client = postgres.Api.CreateClient();

        var page = await client.GetFromJsonAsync<MoviesPage>(
            $"/movies?userId={userId}&searchPart={Uri.EscapeDataString("Дюна")}&genreId={scifi}&sortBy=Rating&sortByDesc=true&page=2&pageSize=1");

        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["Дюна"], page.Items.Select(m => m.Title));
    }

    // Фильтры status (по имени enum) и inWatchlist привязываются из query-строки
    [Theory]
    [InlineData("status=Watched", "Дюна")]
    [InlineData("inWatchlist=true", "Дюна: Часть вторая")]
    public async Task Get_BindsStatusAndWatchlistFilters(string filter, string expectedTitle)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var dune = await db.AddMovieAsync("Дюна");
        var duneTwo = await db.AddMovieAsync("Дюна: Часть вторая");
        await db.AddMovieAsync("Матрица");
        await db.AddUserDataAsync(userId, dune, Status.Watched);
        await db.AddToWatchlistAsync(userId, duneTwo);
        var client = postgres.Api.CreateClient();

        var page = await client.GetFromJsonAsync<MoviesPage>($"/movies?userId={userId}&{filter}");

        Assert.NotNull(page);
        Assert.Equal([expectedTitle], page.Items.Select(m => m.Title));
        Assert.Equal(1, page.TotalCount);
    }

    // Доменное исключение (InvalidPageSizeException) эндпоинт переводит в 400 с текстом ошибки в теле. Это не ошибка биндинга:
    // 0 и -1 — настоящие числа, биндинг их пропускает, а отклоняет handler
    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-5")]
    public async Task Get_NonPositivePageOrPageSize_Returns400WithMessage(string pageParameter)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var client = postgres.Api.CreateClient();

        var response = await client.GetAsync($"/movies?userId={userId}&{pageParameter}");

        await HttpAssert.DomainErrorAsync(response, "Page and PageSize must be greater than 0");
    }

    // Без удаления сидеров в ApiFactory этот тест красный: при поднятии хоста они вставляют каталог, в прогоне без удаления было 3 фильма.
    // Собственная фабрика нужна, чтобы тест не зависел от порядка: общий хост мог быть построен раньше очистки таблиц,
    // и сидеры к этому моменту уже отработали бы незаметно
    [Fact]
    public async Task Factory_DoesNotRunSeeders()
    {
        var db = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(postgres.ConnectionString);

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/movies?userId=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await db.CountAllMoviesAsync());
        await using var context = db.NewContext();
        Assert.Empty(context.Genres);
        Assert.Empty(context.Users);
    }
}
