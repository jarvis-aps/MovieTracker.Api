using Microsoft.Extensions.DependencyInjection;
using MovieTracker.Api.Commands;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Tests;

// Команда целиком: настоящий mediator, handler'ы найдены сканированием сборки (AddMediator) и настоящий Postgres (см. CommandTestProvider).
// Так проверяется и регистрация: если handler не найден или не подключён замер, тест упадёт
[Collection(PostgresCollection.Name)]
public class SetStatusCommandTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Send_SetsStatusAndReturnsPersonalRecord()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = CommandTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new SetStatusCommand(movieId, userId, Status.Watching));

        Assert.Equal(new UserMovieDataDto(movieId, Status.Watching, null, null), result);
        Assert.Equal(Status.Watching, (await db.GetUserDataAsync(userId, movieId))?.Status);
        Assert.Equal(1, await db.CountUserDataAsync(userId, movieId));
    }

    [Fact]
    public async Task Send_UnknownMovieOrUser_ReturnsNullAndCreatesNothing()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = CommandTestProvider.Build(db);
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        Assert.Null(await mediator.Send(new SetStatusCommand(9999, userId, Status.Watched)));
        Assert.Null(await mediator.Send(new SetStatusCommand(movieId, 9999, Status.Watched)));
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    // Замер подключён к команде без единой строки в самом handler'е
    [Fact]
    public async Task AddMediator_ConnectsTimingBehaviorToCommands()
    {
        var db = await postgres.CreateDatabaseAsync();
        await using var provider = CommandTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<SetStatusCommand, UserMovieDataDto?>>();

        Assert.IsType<TimingBehavior<SetStatusCommand, UserMovieDataDto?>>(Assert.Single(behaviors));
    }

    // Повтор команды меняет ту же запись, а не создаёт вторую
    [Fact]
    public async Task Send_RepeatedCommand_UpdatesSingleRowAndDoesNotDuplicate()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = CommandTestProvider.Build(db);

        // каждый вызов в своём scope, как два отдельных запроса
        using var firstScope = provider.CreateScope();
        var first = await firstScope.ServiceProvider.GetRequiredService<IMediator>().Send(new SetStatusCommand(movieId, userId, Status.Watching));
        using var secondScope = provider.CreateScope();
        var second = await secondScope.ServiceProvider.GetRequiredService<IMediator>().Send(new SetStatusCommand(movieId, userId, Status.Watched));

        Assert.Equal(Status.Watching, first?.Status);
        Assert.Equal(Status.Watched, second?.Status);
        Assert.Equal(1, await db.CountUserDataAsync(userId, movieId));
    }
}
