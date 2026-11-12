using Microsoft.Extensions.DependencyInjection;
using MovieTracker.Api.Commands;
using MovieTracker.Api.Mediator;

namespace MovieTracker.Api.Tests;

// Команда «положить в watchlist» целиком: настоящий mediator, handler найден сканированием сборки и настоящий Postgres.
// Сценарии те же, что были у WatchlistServiceTests.AddAsync_*; HTTP-ответы (204 и 404) проверяют WatchlistHttpTests
[Collection(PostgresCollection.Name)]
public class AddToWatchlistCommandTests(PostgresFixture postgres)
{
    private const int ParallelCalls = 30;

    // Как в приложении: один scope (и один контекст) на каждый вызов
    private static async Task<T> SendAsync<T>(ServiceProvider provider, IRequest<T> request)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    [Fact]
    public async Task Send_CalledTwice_ReturnsTrueAndKeepsSingleRow()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = MediatorTestProvider.Build(db);

        var first = await SendAsync(provider, new AddToWatchlistCommand(movieId, userId));
        var second = await SendAsync(provider, new AddToWatchlistCommand(movieId, userId));

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, await db.CountWatchlistAsync(userId, movieId));
    }

    // Регрессия: параллельные PUT одной пары. Проверка AnyAsync их всех пропускает, дубликат останавливает только уникальный индекс,
    // а handler должен превратить эту ошибку в успех. Тест вероятностный: красный прогон доказывает баг, зелёный не доказывает его
    // отсутствие; для точной проверки есть тест ниже
    [Fact]
    public async Task Send_ParallelCalls_AllSucceedAndKeepSingleRow()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = MediatorTestProvider.Build(db);

        var results = await Task.WhenAll(Enumerable.Range(0, ParallelCalls)
            .Select(_ => Task.Run(() => SendAsync(provider, new AddToWatchlistCommand(movieId, userId)))));

        Assert.All(results, Assert.True);
        Assert.Equal(1, await db.CountWatchlistAsync(userId, movieId));
    }

    // Точное воспроизведение гонки: победитель вставил пару, но ещё не подтвердил, проигравший не видит её (проверка существования
    // пропускает), встаёт на уникальном индексе, а после подтверждения получает нарушение. Handler обязан ответить успехом и не создать дубликат.
    // Без перехвата нарушения тест падает каждый раз, а не по вероятности
    [Fact]
    public async Task Send_LoserOfAddRace_ReturnsTrueAndKeepsSingleRow()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = MediatorTestProvider.Build(db);

        await using var winner = await db.InsertWatchlistItemWithoutCommitAsync(userId, movieId);
        var loser = SendAsync(provider, new AddToWatchlistCommand(movieId, userId));
        await db.WaitUntilWriterIsBlockedAsync(loser);

        await winner.CommitAsync();

        Assert.True(await loser);
        Assert.Equal(1, await db.CountWatchlistAsync(userId, movieId));
    }

    [Fact]
    public async Task Send_UnknownMovieOrUser_ReturnsFalseAndAddsNothing()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = MediatorTestProvider.Build(db);

        Assert.False(await SendAsync(provider, new AddToWatchlistCommand(MovieId: 9999, userId)));
        Assert.False(await SendAsync(provider, new AddToWatchlistCommand(movieId, UserId: 9999)));
        Assert.Equal(0, await db.CountWatchlistAsync(userId, movieId));
    }

    // Замер подключён к команде без единой строки в самом handler'е
    [Fact]
    public async Task AddMediator_ConnectsTimingBehaviorToAddToWatchlistCommand()
    {
        var db = await postgres.CreateDatabaseAsync();
        await using var provider = MediatorTestProvider.Build(db);
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<AddToWatchlistCommand, bool>>();

        Assert.Collection(behaviors, b => Assert.IsType<TimingBehavior<AddToWatchlistCommand, bool>>(b), b => Assert.IsType<ValidationBehavior<AddToWatchlistCommand, bool>>(b));
    }
}
