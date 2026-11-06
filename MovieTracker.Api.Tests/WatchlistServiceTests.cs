using Microsoft.Extensions.Logging.Abstractions;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Tests;

[Collection(PostgresCollection.Name)]
public class WatchlistServiceTests(PostgresFixture postgres)
{
    private const int ParallelCalls = 30;
    private const int Rounds = 10;

    private static WatchlistService CreateService(TestDatabase db) =>
        new(db.NewContext(), NullLogger<WatchlistService>.Instance);

    [Fact]
    public async Task AddAsync_CalledTwice_ReturnsTrueAndKeepsSingleRow()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        var first = await CreateService(db).AddAsync(movieId, userId);
        var second = await CreateService(db).AddAsync(movieId, userId);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, await db.CountWatchlistAsync(userId, movieId));
    }

    // Регрессия задачи 3.4: параллельные PUT одной пары. Проверка AnyAsync их всех пропускает,
    // дубликат останавливает только уникальный индекс, а сервис должен превратить эту ошибку в успех
    [Fact]
    public async Task AddAsync_ParallelCalls_AllSucceedAndKeepSingleRow()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        var results = await Task.WhenAll(Enumerable.Range(0, ParallelCalls)
            .Select(_ => Task.Run(() => CreateService(db).AddAsync(movieId, userId))));

        Assert.All(results, Assert.True);
        Assert.Equal(1, await db.CountWatchlistAsync(userId, movieId));
    }

    [Fact]
    public async Task AddAsync_UnknownMovieOrUser_ReturnsFalseAndAddsNothing()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        Assert.False(await CreateService(db).AddAsync(movieId: 9999, userId));
        Assert.False(await CreateService(db).AddAsync(movieId, userId: 9999));
        Assert.Equal(0, await db.CountWatchlistAsync(userId, movieId));
    }

    [Fact]
    public async Task RemoveAsync_CalledTwice_ReturnsTrueAndRowIsGone()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddToWatchlistAsync(userId, movieId);

        var first = await CreateService(db).RemoveAsync(movieId, userId);
        var second = await CreateService(db).RemoveAsync(movieId, userId);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(0, await db.CountWatchlistAsync(userId, movieId));
    }

    // Регрессия задачи 3.4: Remove + SaveChanges падал бы с DbUpdateConcurrencyException, когда строку уже удалил соседний запрос
    [Fact]
    public async Task RemoveAsync_ParallelCalls_AllSucceedAndRowIsGone()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        // Одного раунда мало: гонка срабатывает не каждый раз (проверено мутацией — один раунд пропускал сломанный код)
        for (var round = 0; round < Rounds; round++)
        {
            await db.AddToWatchlistAsync(userId, movieId);

            var results = await Task.WhenAll(Enumerable.Range(0, ParallelCalls)
                .Select(_ => Task.Run(() => CreateService(db).RemoveAsync(movieId, userId))));

            Assert.All(results, Assert.True);
            Assert.Equal(0, await db.CountWatchlistAsync(userId, movieId));
        }
    }

    [Fact]
    public async Task RemoveAsync_UnknownMovieOrUser_ReturnsFalse()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        Assert.False(await CreateService(db).RemoveAsync(movieId: 9999, userId));
        Assert.False(await CreateService(db).RemoveAsync(movieId, userId: 9999));
    }
}
