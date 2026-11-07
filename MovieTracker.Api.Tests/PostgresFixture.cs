using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MovieTracker.Api.Tests;

// Один контейнер Postgres на весь прогон тестов (запуск контейнера — секунды, на каждый тест это было бы слишком долго)
public class PostgresFixture : IAsyncLifetime
{
    // Та же мажорная версия, что у боевого movietracker-db
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    // Новая пустая база с применёнными миграциями — у каждого теста своя, поэтому тесты не влияют друг на друга
    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await command.ExecuteNonQueryAsync();
        }

        // Pooling=false: у каждого теста своя база, а значит свой пул, который держит соединения минутами. Пулы копились от теста
        // к тесту и упирались в лимит Postgres (100): «53300 too many clients». Тестам пул ничего не даёт
        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name, Pooling = false }.ToString();
        var database = new TestDatabase(connectionString);

        await using var context = database.NewContext();
        await context.Database.MigrateAsync();

        return database;
    }
}

// xUnit создаёт фикстуру один раз на коллекцию; все тестовые классы с этим атрибутом делят один контейнер
[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}

public class TestDatabase(string connectionString)
{
    // DbContext не потокобезопасен: параллельным вызовам (тесты гонок) нужен свой контекст каждому
    public MovieTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<MovieTrackerDbContext>().UseNpgsql(connectionString).Options);

    public async Task<int> AddUserAsync(string userName = "user")
    {
        await using var context = NewContext();
        var user = new User(userName);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    public async Task<int> AddMovieAsync(string title, int year = 2000)
    {
        await using var context = NewContext();
        var movie = new Movie(title, year);
        context.Movies.Add(movie);
        await context.SaveChangesAsync();
        return movie.Id;
    }

    // Много фильмов одним SaveChanges — для тестов, которым нужен объём, а не разные данные
    public async Task<List<int>> AddMoviesAsync(string title, int count)
    {
        await using var context = NewContext();
        var movies = Enumerable.Range(0, count).Select(_ => new Movie(title, 2000)).ToList();
        context.Movies.AddRange(movies);
        await context.SaveChangesAsync();
        return movies.Select(m => m.Id).ToList();
    }

    public async Task AddUserDataAsync(int userId, int movieId, Status status, float? rating = null, string? notes = null)
    {
        await using var context = NewContext();
        context.UserMovieData.Add(new UserMovieData(userId, movieId, status) { Rating = rating, Notes = notes });
        await context.SaveChangesAsync();
    }

    public async Task AddToWatchlistAsync(int userId, int movieId)
    {
        await using var context = NewContext();
        context.WatchlistItems.Add(new WatchlistItem(userId, movieId));
        await context.SaveChangesAsync();
    }

    public async Task<UserMovieData?> GetUserDataAsync(int userId, int movieId)
    {
        await using var context = NewContext();
        return await context.UserMovieData.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId && d.MovieId == movieId);
    }

    public async Task<int> CountWatchlistAsync(int userId, int movieId)
    {
        await using var context = NewContext();
        return await context.WatchlistItems.CountAsync(w => w.UserId == userId && w.MovieId == movieId);
    }

    public async Task<int> CountUserDataAsync(int userId, int movieId)
    {
        await using var context = NewContext();
        return await context.UserMovieData.CountAsync(d => d.UserId == userId && d.MovieId == movieId);
    }
}
