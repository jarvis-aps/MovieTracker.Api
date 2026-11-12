using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;

namespace MovieTracker.Api.Tests;

// Три формата 400, которые сейчас есть в API (так сложилось, закреплять каждый как норму никто не обещал: единый формат — отдельный вопрос):
// 1) ProblemDetails без errors: ошибка биндинга (нет параметра, не тот тип, битый JSON), её собирает UseExceptionHandler в Bootstrap.cs,
//    и любой ответ 4xx без тела: его дополняет UseStatusCodePages (вместе с AddProblemDetails) стандартным телом «Bad Request».
//    Из-за этого ответ на дубликат фильма (Results.BadRequest() без текста) для клиента не отличим от ошибки биндинга;
// 2) ValidationProblemDetails с errors по полям: проверка POST /movies;
// 3) голая JSON-строка: доменные исключения (заметка, оценка, страница), эндпоинт отвечает Results.BadRequest(текст)
internal static class HttpAssert
{
    public static async Task<HttpValidationProblemDetails> ValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        return problem;
    }

    // ProblemDetails со статусом 400 и без словаря errors: ошибка биндинга или пустой 400, дополненный до стандартного тела
    public static async Task<HttpValidationProblemDetails> PlainProblemAsync(HttpResponseMessage response)
    {
        var problem = await ValidationProblemAsync(response);
        Assert.Empty(problem.Errors);
        return problem;
    }

    // Доменное исключение: тело — JSON-строка с текстом ошибки
    public static async Task DomainErrorAsync(HttpResponseMessage response, string expectedMessage)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedMessage, await response.Content.ReadFromJsonAsync<string>());
    }
}
