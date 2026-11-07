using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Seeders;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<MovieTrackerDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("MovieTrackerDb")));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddScoped<MovieService>();
builder.Services.AddScoped<UserMovieDataService>();
builder.Services.AddScoped<WatchlistService>();
// MovieService получает watchlist только через интерфейс; пересылка на тот же WatchlistService, чтобы на запрос был один экземпляр
builder.Services.AddScoped<IWatchlistReader>(services => services.GetRequiredService<WatchlistService>());
builder.Services.AddAsyncInitializer<GenresSeeder>();
builder.Services.AddAsyncInitializer<MovieSeeder>();
builder.Services.AddAsyncInitializer<UsersSeeder>();
builder.Services.AddAsyncInitializer<UserMovieDataSeeder>();
builder.Services.AddAsyncInitializer<WatchlistSeeder>();

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is BadHttpRequestException badRequestException)
            context.Response.StatusCode = badRequestException.StatusCode;
        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new ProblemDetailsContext { HttpContext = context });
    });
});

app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();

// ---------- Эндпоинты ----------
// Эндпоинт = HTTP-метод + путь. Каждый Map* ниже связывает один такой маршрут с обработчиком.
// userId приходит query-параметром (?userId=1), пока нет авторизации (этап 4): потом его заменит пользователь из токена.
// Нет обязательного параметра или он неверного типа — 400 в формате ProblemDetails (его собирает UseExceptionHandler выше).

// GET /movies?userId=1[&status=&genreId=&searchPart=&inWatchlist=&sortBy=&sortByDesc=&page=&pageSize=]
// Что делает: отдаёт страницу каталога фильмов в личном виде пользователя userId.
//   Фильтры: status, genreId, searchPart (часть названия, без учёта регистра), inWatchlist.
//   Сортировка: sortBy (Title, Status, Rating; Genre — заглушка, сортирует как Title) и sortByDesc.
//   Страницы: page и pageSize (по умолчанию 1 и 10).
// Что отдаёт: 200 и список MovieDto (id, название, жанры, год, оценка, статус, inWatchlist, заметка); пустой результат — тоже 200 с [].
//   Фильм без личной записи приходит как NotWatched без оценки и заметки. 400 — page или pageSize <= 0.
app.MapGet("/movies", async (MovieService service, [AsParameters]MovieQueryParameters movieQueryParameters) =>
{
    List<MovieDto> result;
    
    try
    {
        result = await service.GetMoviesAsync(movieQueryParameters);
    }
    catch (InvalidPageSizeException exception)
    {
        return Results.BadRequest(exception.ErrorMessage);
    }
    
    return Results.Ok(result);
});

// PATCH /movies/{id}/status?userId=1&status=Watched
// Что делает: ставит пользователю статус фильма (NotWatched, Watching, Watched). Если личной записи ещё нет — создаёт её.
// Что отдаёт: 200 и личную запись UserMovieDataDto (movieId, статус, оценка, заметка) без данных каталога.
//   404 — нет такого фильма или пользователя; 400 — статус не из списка.
app.MapPatch("/movies/{id}/status", async (int id, int userId, Status status, UserMovieDataService service) =>
{
    var result = await service.UpdateStatusAsync(id, userId, status);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

// PATCH /movies/{id}/notes?userId=1&notes=текст
// Что делает: записывает пользователю заметку к фильму, заменяя прежнюю. Если личной записи ещё нет — создаёт её со статусом NotWatched.
// Что отдаёт: 200 и личную запись UserMovieDataDto (с заметкой); 404 — нет такого фильма или пользователя.
app.MapPatch("/movies/{id}/notes", async (int id, int userId, string notes, UserMovieDataService service) =>
{
    var result = await service.UpdateNotesAsync(id, userId, notes);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

// PATCH /movies/{id}/rating?userId=1&rating=7.5
// Что делает: ставит пользователю оценку фильма. Если личной записи ещё нет — создаёт её со статусом NotWatched.
// Что отдаёт: 200 и личную запись UserMovieDataDto (с оценкой); 404 — нет такого фильма или пользователя;
//   400 — оценка вне диапазона 0–10 (текст ошибки в теле).
app.MapPatch("/movies/{id}/rating", async (int id, int userId, float rating, UserMovieDataService service) =>
{
    UserMovieDataDto? result;
    
    try
    {
        result = await service.UpdateRatingAsync(id, userId, rating);
    }
    catch (InvalidRatingException exception)
    {
        return Results.BadRequest(exception.ErrorMessage);
    }

    return result is null ? Results.NotFound() : Results.Ok(result);
});

// POST /movies   тело JSON: { "title", "year", "genresId": [1, 2], "status", "notes" }
// Что делает: добавляет фильм в общий каталог (не в личные данные). Жанры берутся по id, несуществующие id молча пропускаются.
//   Поля status и notes из тела сейчас не используются: личные данные пишутся только через PATCH.
// Что отдаёт: 201, заголовок Location: /movies/{id} (отдельного GET по id пока нет) и MovieDto нового фильма
//   (оценка, статус и заметка — null, inWatchlist — false). 400 без тела — фильм с таким названием уже есть.
app.MapPost("/movies", async (MovieService service, NewMovie newMovie) =>
{
    var result = await service.CreateMovieAsync(newMovie);
    return result is null ? Results.BadRequest() : Results.Created($"/movies/{result.Id}", result);
});

// PUT /watchlist/{movieId}?userId=1
// Что делает: кладёт фильм в список «Хочу посмотреть» пользователя. Идемпотентен: повторный или параллельный вызов не создаёт дубликат и не падает.
// Что отдаёт: 204 без тела (в том числе если фильм уже был в списке); 404 — нет такого фильма или пользователя.
app.MapPut("/watchlist/{movieId}", async (int movieId, int userId, WatchlistService service) =>
{
    var found = await service.AddAsync(movieId, userId);
    return found ? Results.NoContent() : Results.NotFound();
});

// DELETE /watchlist/{movieId}?userId=1
// Что делает: убирает фильм из списка «Хочу посмотреть» пользователя. Идемпотентен: если фильма там не было, это не ошибка.
// Что отдаёт: 204 без тела; 404 — нет такого фильма или пользователя.
app.MapDelete("/watchlist/{movieId}", async (int movieId, int userId, WatchlistService service) =>
{
    var found = await service.RemoveAsync(movieId, userId);
    return found ? Results.NoContent() : Results.NotFound();
});

await app.InitAndRunAsync();
