using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTracker.Api.Commands;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Tests;

// Команда целиком: настоящий mediator, handler'ы найдены сканированием сборки (AddMediator) и настоящий Postgres.
// Так проверяется и регистрация: если handler не найден или не подключён замер, тест упадёт
[Collection(PostgresCollection.Name)]
public class SetStatusCommandTests(PostgresFixture postgres)
{
    // Как в приложении: один контекст на запрос (scope), mediator и handler получают его через DI
    private static ServiceProvider BuildProvider(TestDatabase db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped(_ => db.NewContext());
        services.AddScoped<UserMovieDataService>();
        services.AddMediator(typeof(SetStatusHandler).Assembly);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Send_SetsStatusAndReturnsPersonalRecord()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = BuildProvider(db);
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
        await using var provider = BuildProvider(db);
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
        await using var provider = BuildProvider(db);
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<SetStatusCommand, UserMovieDataDto?>>();

        Assert.IsType<TimingBehavior<SetStatusCommand, UserMovieDataDto?>>(Assert.Single(behaviors));
    }
}
