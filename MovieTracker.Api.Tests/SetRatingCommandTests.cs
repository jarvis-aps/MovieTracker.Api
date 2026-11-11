using Microsoft.Extensions.DependencyInjection;
using MovieTracker.Api.Commands;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Tests;

// Команда оценки целиком: настоящий mediator, handler найден сканированием сборки и настоящий Postgres
[Collection(PostgresCollection.Name)]
public class SetRatingCommandTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Send_SetsRatingAndReturnsPersonalRecord()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = CommandTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new SetRatingCommand(movieId, userId, 7.5f));

        Assert.Equal(new UserMovieDataDto(movieId, Status.NotWatched, 7.5f, null), result);
        Assert.Equal(7.5f, (await db.GetUserDataAsync(userId, movieId))?.Rating);
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

        Assert.Null(await mediator.Send(new SetRatingCommand(9999, userId, 5)));
        Assert.Null(await mediator.Send(new SetRatingCommand(movieId, 9999, 5)));
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    // Ошибка проходит сквозь mediator и замер как есть: превратить её в 400 по-прежнему должен эндпоинт
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(10.1f)]
    [InlineData(float.NaN)]
    public async Task Send_RatingOutOfRange_ExceptionPassesThroughMediatorAndSavesNothing(float rating)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = CommandTestProvider.Build(db);
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<InvalidRatingException>(() =>
            scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SetRatingCommand(movieId, userId, rating)));

        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    // Замер подключён к команде без единой строки в самом handler'е
    [Fact]
    public async Task AddMediator_ConnectsTimingBehaviorToRatingCommand()
    {
        var db = await postgres.CreateDatabaseAsync();
        await using var provider = CommandTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<SetRatingCommand, UserMovieDataDto?>>();

        Assert.IsType<TimingBehavior<SetRatingCommand, UserMovieDataDto?>>(Assert.Single(behaviors));
    }
}
