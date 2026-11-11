using Microsoft.Extensions.DependencyInjection;
using MovieTracker.Api.Commands;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Tests;

// Команда заметки целиком: настоящий mediator, handler найден сканированием сборки и настоящий Postgres
[Collection(PostgresCollection.Name)]
public class SetNotesCommandTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Send_SetsNotesAndReturnsPersonalRecord()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = CommandTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new SetNotesCommand(movieId, userId, "Смотреть в кино"));

        Assert.Equal(new UserMovieDataDto(movieId, Status.NotWatched, null, "Смотреть в кино"), result);
        Assert.Equal("Смотреть в кино", (await db.GetUserDataAsync(userId, movieId))?.Notes);
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

        Assert.Null(await mediator.Send(new SetNotesCommand(9999, userId, "заметка")));
        Assert.Null(await mediator.Send(new SetNotesCommand(movieId, 9999, "заметка")));
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    // Ошибка проходит сквозь mediator и замер как есть: превратить её в 400 по-прежнему должен эндпоинт
    [Fact]
    public async Task Send_TooLongNotes_ExceptionPassesThroughMediatorAndSavesNothing()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = CommandTestProvider.Build(db);
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<InvalidNotesException>(() =>
            scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SetNotesCommand(movieId, userId, new string('a', 501))));

        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    // Замер подключён к команде без единой строки в самом handler'е
    [Fact]
    public async Task AddMediator_ConnectsTimingBehaviorToNotesCommand()
    {
        var db = await postgres.CreateDatabaseAsync();
        await using var provider = CommandTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<SetNotesCommand, UserMovieDataDto?>>();

        Assert.IsType<TimingBehavior<SetNotesCommand, UserMovieDataDto?>>(Assert.Single(behaviors));
    }
}
