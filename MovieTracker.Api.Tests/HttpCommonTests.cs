namespace MovieTracker.Api.Tests;

// То, что общее для всех эндпоинтов: ошибки биндинга параметров (UseExceptionHandler в Bootstrap.cs) и CORS для фронтенда
[Collection(PostgresCollection.Name)]
public class HttpCommonTests(PostgresFixture postgres)
{
    // Ошибки биндинга (нет обязательного параметра, значение не того типа, имя не из enum) отвечают 400 в формате ProblemDetails без errors.
    // Запрос до базы не доходит: поэтому ids вымышленные, а таблицы пусты. Без UseExceptionHandler это был бы 500
    [Theory]
    [InlineData("GET", "/movies")]                                          // нет userId
    [InlineData("GET", "/movies?userId=abc")]                               // userId не число
    [InlineData("GET", "/movies?userId=1&page=abc")]                        // page не число
    [InlineData("GET", "/movies?userId=1&status=Banana")]                   // статус не из списка
    [InlineData("PATCH", "/movies/1/status?userId=1")]                      // нет status
    [InlineData("PATCH", "/movies/1/status?userId=1&status=Banana")]        // статус не из списка
    [InlineData("PATCH", "/movies/1/status?status=Watched")]                // нет userId
    [InlineData("PATCH", "/movies/abc/status?userId=1&status=Watched")]     // id фильма не число
    [InlineData("PATCH", "/movies/1/notes?userId=1")]                       // нет notes
    [InlineData("PATCH", "/movies/1/rating?userId=1")]                      // нет rating
    [InlineData("PATCH", "/movies/1/rating?userId=1&rating=abc")]           // rating не число
    [InlineData("PUT", "/watchlist/1")]                                     // нет userId
    [InlineData("DELETE", "/watchlist/1")]                                  // нет userId
    public async Task BindingError_Returns400Problem(string method, string url)
    {
        await postgres.CreateDatabaseAsync();
        var client = postgres.Api.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        await HttpAssert.PlainProblemAsync(response);
    }

    // CORS: фронтенд на http://localhost:5173 получает разрешающий заголовок, любой другой источник — нет
    [Theory]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("http://evil.example", null)]
    public async Task Cors_AllowsOnlyFrontendOrigin(string origin, string? expectedAllowOrigin)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var client = postgres.Api.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/movies?userId={userId}");
        request.Headers.Add("Origin", origin);

        var response = await client.SendAsync(request);

        var actual = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;
        Assert.Equal(expectedAllowOrigin, actual);
    }
}
