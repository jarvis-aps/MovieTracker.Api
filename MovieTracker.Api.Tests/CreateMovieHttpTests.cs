using System.Net;
using System.Net.Http.Json;
using System.Text;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Tests;

// POST /movies на уровне HTTP: тело JSON биндится прямо в команду CreateMovieCommand, дальше mediator (ValidationBehavior, handler).
// Правила проверки и логику дубликата проверяют CreateMovieValidatorTests и CreateMovieCommandTests; здесь — коды ответов, заголовки и форма тела
[Collection(PostgresCollection.Name)]
public class CreateMovieHttpTests(PostgresFixture postgres)
{
    // 201, заголовок Location указывает на /movies/{id} (отдельного GET по id пока нет, но адрес уже часть ответа), в теле — созданный фильм
    // без личных данных: оценка, статус и заметка null, inWatchlist false
    [Fact]
    public async Task Post_ValidMovie_Returns201WithLocationAndMovie()
    {
        var db = await postgres.CreateDatabaseAsync();
        var genreId = await db.AddGenreAsync("Sci-Fi");
        var client = postgres.Api.CreateClient();

        var response = await client.PostAsJsonAsync("/movies", new { title = "Дюна", year = 2021, genresId = new[] { genreId } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var movie = await response.Content.ReadFromJsonAsync<MovieDto>();
        Assert.NotNull(movie);
        Assert.Equal($"/movies/{movie.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("Дюна", movie.Title);
        Assert.Equal(2021, movie.Year);
        Assert.Equal([new GenreDto(genreId, "Sci-Fi")], movie.Genres);
        Assert.Null(movie.rating);
        Assert.Null(movie.status);
        Assert.False(movie.InWatchlist);
        Assert.Null(movie.Notes);
        Assert.Equal(1, await db.CountMoviesAsync("Дюна"));
    }

    // Дубликат (то же название без учёта регистра и пробелов по краям и тот же год): handler возвращает null, эндпоинт отвечает
    // Results.BadRequest() без текста, а UseStatusCodePages дополняет пустой ответ стандартным ProblemDetails «Bad Request».
    // Причина (дубликат) клиенту нигде не названа: тот же формат, что у ошибки биндинга. Текст заголовка тела тест намеренно не сверяет:
    // отсутствие причины — недостаток, а не контракт, и его исправление не должно ломать тест (формат 400 останется ProblemDetails)
    [Fact]
    public async Task Post_Duplicate_Returns400ProblemAndCreatesNothing()
    {
        var db = await postgres.CreateDatabaseAsync();
        await db.AddMovieAsync("Матрица", 1999);
        var client = postgres.Api.CreateClient();

        var response = await client.PostAsJsonAsync("/movies", new { title = "  матрица ", year = 1999, genresId = Array.Empty<int>() });

        await HttpAssert.PlainProblemAsync(response);
        Assert.Equal(1, await db.CountAllMoviesAsync());
    }

    // Проверка полей: RequestValidationException из ValidationBehavior эндпоинт переводит в ValidationProblemDetails со словарём errors
    // по полям и всеми ошибками сразу. Пустой объект нарушает три правила (название, год, список жанров)
    [Fact]
    public async Task Post_InvalidFields_Returns400WithAllErrorsByField()
    {
        var db = await postgres.CreateDatabaseAsync();
        var client = postgres.Api.CreateClient();

        var response = await client.PostAsync("/movies", new StringContent("{}", Encoding.UTF8, "application/json"));

        var problem = await HttpAssert.ValidationProblemAsync(response);
        Assert.Equivalent(new[] { "title", "year", "genresId" }, problem.Errors.Keys);
        Assert.Equal(0, await db.CountAllMoviesAsync());
    }

    // Не JSON: биндинг тела отвечает 415 до того, как запрос доходит до mediator
    [Fact]
    public async Task Post_NotJsonContentType_Returns415()
    {
        await postgres.CreateDatabaseAsync();
        var client = postgres.Api.CreateClient();

        var response = await client.PostAsync("/movies", new StringContent("Дюна", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // Битый JSON — ошибка биндинга: ProblemDetails без errors, не ValidationProblemDetails проверки полей
    [Fact]
    public async Task Post_BrokenJson_Returns400BindingProblem()
    {
        var db = await postgres.CreateDatabaseAsync();
        var client = postgres.Api.CreateClient();

        var response = await client.PostAsync("/movies", new StringContent("""{"title":""", Encoding.UTF8, "application/json"));

        await HttpAssert.PlainProblemAsync(response);
        Assert.Equal(0, await db.CountAllMoviesAsync());
    }
}
