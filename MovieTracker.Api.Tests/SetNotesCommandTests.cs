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
        await using var provider = MediatorTestProvider.Build(db);
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
        await using var provider = MediatorTestProvider.Build(db);
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
        await using var provider = MediatorTestProvider.Build(db);
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
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<SetNotesCommand, UserMovieDataDto?>>();

        Assert.Collection(behaviors, b => Assert.IsType<TimingBehavior<SetNotesCommand, UserMovieDataDto?>>(b), b => Assert.IsType<ValidationBehavior<SetNotesCommand, UserMovieDataDto?>>(b));
    }

    // Решение 3.12: заметка до 500 символов включительно
    [Fact]
    public async Task Send_ExactlyMaxLength_IsAccepted()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        var notes = new string('я', 500);
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SetNotesCommand(movieId, userId, notes));

        Assert.Equal(notes, result?.Notes);
        Assert.Equal(notes, (await db.GetUserDataAsync(userId, movieId))?.Notes);
    }

    // Слишком длинную заметку не обрезаем и не пишем частично: прежняя остаётся как была
    [Fact]
    public async Task Send_TooLongNotes_KeepsPreviousNotes()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddUserDataAsync(userId, movieId, Status.NotWatched, notes: "прежняя заметка");
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<InvalidNotesException>(() =>
            scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SetNotesCommand(movieId, userId, new string('я', 501))));

        Assert.Equal("прежняя заметка", (await db.GetUserDataAsync(userId, movieId))?.Notes);
    }

    // Порядок проверок: сначала «фильм и пользователь есть» (null, то есть 404 в эндпоинте), потом длина (ошибка, то есть 400).
    // Поменять их местами значит сменить ответ клиенту: вместо 404 придёт 400
    [Fact]
    public async Task Send_UnknownMovieOrUserAndTooLongNotes_ReturnsNullNotException()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var tooLong = new string('a', 501);

        Assert.Null(await mediator.Send(new SetNotesCommand(9999, userId, tooLong)));
        Assert.Null(await mediator.Send(new SetNotesCommand(movieId, 9999, tooLong)));
    }
}
