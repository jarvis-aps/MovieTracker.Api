using Microsoft.Extensions.DependencyInjection;
using MovieTracker.Api.Commands;
using MovieTracker.Api.Mediator;

namespace MovieTracker.Api.Tests;

// Команда «убрать из watchlist» целиком: настоящий mediator, handler найден сканированием сборки и настоящий Postgres.
// Сценарии те же, что были у WatchlistServiceTests.RemoveAsync_*; HTTP-ответы (204 и 404) проверяют WatchlistHttpTests
[Collection(PostgresCollection.Name)]
public class RemoveFromWatchlistCommandTests(PostgresFixture postgres)
{
    private const int ParallelCalls = 30;
    private const int Rounds = 10;

    // Как в приложении: один scope (и один контекст) на каждый вызов
    private static async Task<T> SendAsync<T>(ServiceProvider provider, IRequest<T> request)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    [Fact]
    public async Task Send_CalledTwice_ReturnsTrueAndRowIsGone()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddToWatchlistAsync(userId, movieId);
        await using var provider = MediatorTestProvider.Build(db);

        var first = await SendAsync(provider, new RemoveFromWatchlistCommand(movieId, userId));
        var second = await SendAsync(provider, new RemoveFromWatchlistCommand(movieId, userId));

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(0, await db.CountWatchlistAsync(userId, movieId));
    }

    // Регрессия: Remove + SaveChanges падал бы с DbUpdateConcurrencyException, когда строку уже удалил соседний запрос
    [Fact]
    public async Task Send_ParallelCalls_AllSucceedAndRowIsGone()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = MediatorTestProvider.Build(db);

        // Одного раунда мало: гонка срабатывает не каждый раз (проверено мутацией — один раунд пропускал сломанный код)
        for (var round = 0; round < Rounds; round++)
        {
            await db.AddToWatchlistAsync(userId, movieId);

            var results = await Task.WhenAll(Enumerable.Range(0, ParallelCalls)
                .Select(_ => Task.Run(() => SendAsync(provider, new RemoveFromWatchlistCommand(movieId, userId)))));

            Assert.All(results, Assert.True);
            Assert.Equal(0, await db.CountWatchlistAsync(userId, movieId));
        }
    }

    [Fact]
    public async Task Send_UnknownMovieOrUser_ReturnsFalse()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = MediatorTestProvider.Build(db);

        Assert.False(await SendAsync(provider, new RemoveFromWatchlistCommand(MovieId: 9999, userId)));
        Assert.False(await SendAsync(provider, new RemoveFromWatchlistCommand(movieId, UserId: 9999)));
    }

    // Замер подключён к команде без единой строки в самом handler'е
    [Fact]
    public async Task AddMediator_ConnectsTimingBehaviorToRemoveFromWatchlistCommand()
    {
        var db = await postgres.CreateDatabaseAsync();
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<RemoveFromWatchlistCommand, bool>>();

        Assert.Collection(behaviors, b => Assert.IsType<TimingBehavior<RemoveFromWatchlistCommand, bool>>(b), b => Assert.IsType<ValidationBehavior<RemoveFromWatchlistCommand, bool>>(b));
    }
}
