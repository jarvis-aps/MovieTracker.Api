using System.Net;
using System.Net.Http.Json;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Tests;

// PATCH /movies/{id}/status, /notes и /rating на уровне HTTP. Логику записи (создание или обновление личной записи, гонка первого PATCH,
// границы) проверяют тесты команд; здесь — коды ответов и форма тела, а главное перевод доменных исключений в 400,
// который живёт в Bootstrap.cs и которого тесты команд не видят
[Collection(PostgresCollection.Name)]
public class PersonalDataHttpTests(PostgresFixture postgres)
{
    private const int MaxNotesLength = 500;

    // ---------- status ----------

    [Fact]
    public async Task PatchStatus_Returns200WithPersonalRecord()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();

        var response = await client.PatchAsync($"/movies/{movieId}/status?userId={userId}&status=Watched", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new UserMovieDataDto(movieId, Status.Watched, null, null), await response.Content.ReadFromJsonAsync<UserMovieDataDto>());
        Assert.Equal(Status.Watched, (await db.GetUserDataAsync(userId, movieId))?.Status);
    }

    // Нет фильма или нет пользователя — 404 (handler вернул null), личная запись не создаётся
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PatchStatus_UnknownMovieOrUser_Returns404(bool unknownMovie)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();

        var response = await client.PatchAsync(
            $"/movies/{(unknownMovie ? 9999 : movieId)}/status?userId={(unknownMovie ? userId : 9999)}&status=Watched", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    // ---------- notes ----------

    [Fact]
    public async Task PatchNotes_Returns200WithPersonalRecord()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();

        var response = await client.PatchAsync($"/movies/{movieId}/notes?userId={userId}&notes={Uri.EscapeDataString("Отличный фильм")}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new UserMovieDataDto(movieId, Status.NotWatched, null, "Отличный фильм"), await response.Content.ReadFromJsonAsync<UserMovieDataDto>());
        Assert.Equal("Отличный фильм", (await db.GetUserDataAsync(userId, movieId))?.Notes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PatchNotes_UnknownMovieOrUser_Returns404(bool unknownMovie)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();

        var response = await client.PatchAsync(
            $"/movies/{(unknownMovie ? 9999 : movieId)}/notes?userId={(unknownMovie ? userId : 9999)}&notes=текст", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    // InvalidNotesException эндпоинт переводит в 400 с текстом в теле; заметка не обрезается и не сохраняется
    [Fact]
    public async Task PatchNotes_TooLong_Returns400WithMessageAndSavesNothing()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();

        var response = await client.PatchAsync($"/movies/{movieId}/notes?userId={userId}&notes={new string('я', MaxNotesLength + 1)}", null);

        await HttpAssert.DomainErrorAsync(response, "Notes must be at most 500 characters");
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    // ---------- rating ----------

    [Fact]
    public async Task PatchRating_Returns200WithPersonalRecord()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();

        var response = await client.PatchAsync($"/movies/{movieId}/rating?userId={userId}&rating=7.5", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new UserMovieDataDto(movieId, Status.NotWatched, 7.5f, null), await response.Content.ReadFromJsonAsync<UserMovieDataDto>());
        Assert.Equal(7.5f, (await db.GetUserDataAsync(userId, movieId))?.Rating);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PatchRating_UnknownMovieOrUser_Returns404(bool unknownMovie)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();

        var response = await client.PatchAsync(
            $"/movies/{(unknownMovie ? 9999 : movieId)}/rating?userId={(unknownMovie ? userId : 9999)}&rating=5", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    // InvalidRatingException эндпоинт переводит в 400 с текстом в теле; оценка вне 0–10 и NaN не сохраняются.
    // NaN — настоящее значение float для биндинга (не ошибка параметра), отклоняет его handler
    [Theory]
    [InlineData("-0.1")]
    [InlineData("10.1")]
    [InlineData("NaN")]
    public async Task PatchRating_OutOfRange_Returns400WithMessageAndSavesNothing(string rating)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();

        var response = await client.PatchAsync($"/movies/{movieId}/rating?userId={userId}&rating={rating}", null);

        await HttpAssert.DomainErrorAsync(response, "Rating must be between 0 and 10");
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }
}
