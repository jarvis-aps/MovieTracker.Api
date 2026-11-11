using Microsoft.Extensions.DependencyInjection;
using MovieTracker.Api.Commands;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using MovieTracker.Api.Queries;

namespace MovieTracker.Api.Tests;

// Добавление фильма целиком: настоящий mediator, handler найден сканированием сборки и настоящий Postgres. Раньше у POST /movies тестов не было
[Collection(PostgresCollection.Name)]
public class CreateMovieCommandTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Send_CreatesMovieWithItsGenresAndReturnsIt()
    {
        var db = await postgres.CreateDatabaseAsync();
        var action = await db.AddGenreAsync("Action");
        var horror = await db.AddGenreAsync("Horror");
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new CreateMovieCommand("Матрица", 1999, [action, horror]));

        Assert.NotNull(result);
        Assert.Equal("Матрица", result.Title);
        Assert.Equal(1999, result.Year);
        Assert.Equal([new GenreDto(action, "Action"), new GenreDto(horror, "Horror")], result.Genres);
        // личных данных у только что добавленного фильма нет
        Assert.Null(result.rating);
        Assert.Null(result.status);
        Assert.False(result.InWatchlist);
        Assert.Null(result.Notes);
        Assert.Equal(1, await db.CountMoviesAsync("Матрица"));
    }

    // Несуществующие id жанров молча пропускаются: фильм создаётся с теми жанрами, что нашлись
    [Fact]
    public async Task Send_UnknownGenreIds_AreSkipped()
    {
        var db = await postgres.CreateDatabaseAsync();
        var action = await db.AddGenreAsync("Action");
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new CreateMovieCommand("Матрица", 1999, [action, 9999, -1]));

        Assert.Equal([new GenreDto(action, "Action")], result?.Genres);
    }

    [Fact]
    public async Task Send_NoGenres_CreatesMovieWithEmptyGenres()
    {
        var db = await postgres.CreateDatabaseAsync();
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new CreateMovieCommand("Матрица", 1999, []));

        Assert.NotNull(result);
        Assert.Empty(result.Genres);
        Assert.Equal(1, await db.CountMoviesAsync("Матрица"));
    }

    // Фильм с таким названием и годом уже есть: ответ null (эндпоинт отвечает 400), второй не создаётся
    [Fact]
    public async Task Send_ExistingTitleAndYear_ReturnsNullAndCreatesNothing()
    {
        var db = await postgres.CreateDatabaseAsync();
        await db.AddMovieAsync("Дюна", year: 2021);
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new CreateMovieCommand("Дюна", 2021, []));

        Assert.Null(result);
        Assert.Equal(1, await db.CountMoviesAsync("Дюна"));
    }

    // Решение 3.20: дубликат это пара (название, год), поэтому ремейк с тем же названием и другим годом создаётся.
    // Раньше дубликатом считалось одно название, и такой запрос отвечал 400
    [Fact]
    public async Task Send_SameTitleDifferentYear_CreatesTheRemake()
    {
        var db = await postgres.CreateDatabaseAsync();
        await db.AddMovieAsync("Дюна", year: 1984);
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new CreateMovieCommand("Дюна", 2021, []));

        Assert.NotNull(result);
        Assert.Equal(2021, result.Year);
        Assert.Equal(2, await db.CountMoviesAsync("Дюна"));
    }

    // Другое название при том же годе дубликатом не считается: пара сравнивается целиком, а не по одной колонке
    [Fact]
    public async Task Send_DifferentTitleSameYear_IsCreated()
    {
        var db = await postgres.CreateDatabaseAsync();
        await db.AddMovieAsync("Дюна", year: 2021);
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new CreateMovieCommand("Матрица", 2021, []));

        Assert.NotNull(result);
        Assert.Equal(1, await db.CountMoviesAsync("Матрица"));
        Assert.Equal(1, await db.CountMoviesAsync("Дюна"));
    }

    // Гонка двух одинаковых POST, воспроизведённая точно, а не случайно. «Победитель» вставил пару (название, год) и ещё не завершил
    // транзакцию: «проигравший» через mediator не видит эту строку, проходит проверку на дубликат, пытается вставить свою и встаёт
    // на уникальном индексе. Когда победитель завершается, индекс отклоняет вставку проигравшего, и handler обязан ответить null
    // (как на обычный дубликат), а не упасть с ошибкой и не создать второй фильм
    [Fact]
    public async Task Send_LoserOfCreateRace_ReturnsNullAndCreatesNoDuplicate()
    {
        var db = await postgres.CreateDatabaseAsync();
        await using var provider = MediatorTestProvider.Build(db);

        await using var winner = await db.InsertMovieWithoutCommitAsync("Матрица", 1999);
        using var scope = provider.CreateScope();
        var loser = scope.ServiceProvider.GetRequiredService<IMediator>().Send(new CreateMovieCommand("Матрица", 1999, []));
        await db.WaitUntilWriterIsBlockedAsync(loser);

        await winner.CommitAsync();
        var result = await loser;

        Assert.Null(result);
        Assert.Equal(1, await db.CountMoviesAsync("Матрица"));
    }

    // Добавленный фильм виден в каталоге вместе со своими жанрами: чтение и запись видят одну базу
    [Fact]
    public async Task Send_CreatedMovie_AppearsInCatalogWithItsGenres()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var action = await db.AddGenreAsync("Action");
        await using var provider = MediatorTestProvider.Build(db);

        using (var createScope = provider.CreateScope())
            await createScope.ServiceProvider.GetRequiredService<IMediator>().Send(new CreateMovieCommand("Матрица", 1999, [action]));

        using var readScope = provider.CreateScope();
        var page = await readScope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetMoviesQuery(userId));

        var movie = Assert.Single(page.Items);
        Assert.Equal("Матрица", movie.Title);
        Assert.Equal([new GenreDto(action, "Action")], movie.Genres);
        Assert.Equal(1, page.TotalCount);
    }

    // Замер подключён к команде без единой строки в самом handler'е
    [Fact]
    public async Task AddMediator_ConnectsTimingBehaviorToCreateMovieCommand()
    {
        var db = await postgres.CreateDatabaseAsync();
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<CreateMovieCommand, MovieDto?>>();

        Assert.IsType<TimingBehavior<CreateMovieCommand, MovieDto?>>(Assert.Single(behaviors));
    }
}
