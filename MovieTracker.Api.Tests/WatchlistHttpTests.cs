using System.Net;

namespace MovieTracker.Api.Tests;

// PUT и DELETE /watchlist/{movieId} на уровне HTTP. Идемпотентность и гонки проверяют AddToWatchlistCommandTests и RemoveFromWatchlistCommandTests; здесь — коды ответов:
// 204 без тела (в том числе когда делать было нечего) и 404 для несуществующего фильма или пользователя
[Collection(PostgresCollection.Name)]
public class WatchlistHttpTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Put_NewItem_Returns204WithoutBodyAndAddsToWatchlist()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();

        var response = await client.PutAsync($"/watchlist/{movieId}?userId={userId}", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await db.CountWatchlistAsync(userId, movieId));
    }

    // Повторный PUT — тоже 204, а не 409 и не 500: фильм уже в списке, дубликата нет
    [Fact]
    public async Task Put_AlreadyInWatchlist_Returns204AndKeepsOneItem()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddToWatchlistAsync(userId, movieId);
        var client = postgres.Api.CreateClient();

        var response = await client.PutAsync($"/watchlist/{movieId}?userId={userId}", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await db.CountWatchlistAsync(userId, movieId));
    }

    [Fact]
    public async Task Delete_ItemInWatchlist_Returns204WithoutBodyAndRemovesIt()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddToWatchlistAsync(userId, movieId);
        var client = postgres.Api.CreateClient();

        var response = await client.DeleteAsync($"/watchlist/{movieId}?userId={userId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await db.CountWatchlistAsync(userId, movieId));
    }

    // Фильма в списке не было — тоже 204: пользователь хотел, чтобы его там не было, и его там нет
    [Fact]
    public async Task Delete_ItemNotInWatchlist_Returns204()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();

        var response = await client.DeleteAsync($"/watchlist/{movieId}?userId={userId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // Нет фильма или нет пользователя — 404 у обоих методов, и ничего не создаётся
    [Theory]
    [InlineData("PUT", true)]
    [InlineData("PUT", false)]
    [InlineData("DELETE", true)]
    [InlineData("DELETE", false)]
    public async Task UnknownMovieOrUser_Returns404(string method, bool unknownMovie)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var client = postgres.Api.CreateClient();
        var request = new HttpRequestMessage(
            new HttpMethod(method), $"/watchlist/{(unknownMovie ? 9999 : movieId)}?userId={(unknownMovie ? userId : 9999)}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await db.CountWatchlistAsync(userId, movieId));
    }
}
