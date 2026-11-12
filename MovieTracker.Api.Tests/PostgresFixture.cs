using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace MovieTracker.Api.Tests;

// Один контейнер Postgres и одна база на весь прогон тестов: запуск контейнера и миграции — секунды, на каждый тест это было бы слишком долго.
// Тесты не влияют друг на друга за счёт того, что Respawn очищает таблицы перед каждым тестом
public class PostgresFixture : IAsyncLifetime
{
    // Та же мажорная версия, что у боевого movietracker-db
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    private TestDatabase _database = null!;
    private Respawner _respawner = null!;
    private ApiFactory? _api;

    // Приложение для HTTP-тестов: строится при первом обращении (тестам без HTTP оно не нужно) и живёт до конца прогона.
    // Хост поднимается дольше, чем создаётся клиент, поэтому один на все тесты; HttpClient у него дешёвый, берётся на каждый тест.
    // Тесты одной коллекции идут по очереди, поэтому обычного «если ещё нет — создать» достаточно
    public ApiFactory Api => _api ??= new ApiFactory(ConnectionString);

    // Нужна тесту, которому требуется собственное приложение, а не общее (проверка, что сидеры не запускаются)
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Пул соединений оставлен по умолчанию: база одна, значит и пул один (раньше у каждого теста была своя база и свой пул,
        // пулы копились и упирались в лимит Postgres: «53300 too many clients»)
        _database = new TestDatabase(_container.GetConnectionString());

        await using (var context = _database.NewContext())
            await context.Database.MigrateAsync();

        // Respawn один раз читает схему и строит команду очистки: truncate всех таблиц схемы с cascade (проверено по Respawner.DeleteSql).
        // История миграций — не данные теста, на неё нет внешних ключей, поэтому её можно исключить. Исключать таблицу, которая ссылается
        // на очищаемые (например, UserMovieData), бесполезно: cascade всё равно очистит её вместе с родителями
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Table("__EFMigrationsHistory")]
        });
    }

    public async Task DisposeAsync()
    {
        // Сначала приложение (оно держит соединения с базой), потом контейнер
        if (_api is not null)
            await _api.DisposeAsync();

        await _container.DisposeAsync();
    }

    // Очищает все таблицы (схема остаётся) и отдаёт ту же базу: каждый тест начинает с пустых таблиц.
    // Тесты одной коллекции идут по очереди, поэтому очистка не может задеть работающий тест
    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);

        return _database;
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

    // Фильм сразу с жанрами (id уже существующих жанров)
    public async Task<int> AddMovieWithGenresAsync(string title, params int[] genreIds)
    {
        await using var context = NewContext();
        var movie = new Movie(title, 2000) { Genres = await context.Genres.Where(g => genreIds.Contains(g.Id)).ToListAsync() };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();
        return movie.Id;
    }

    public async Task<int> AddGenreAsync(string name)
    {
        await using var context = NewContext();
        var genre = new Genre(name);
        context.Genres.Add(genre);
        await context.SaveChangesAsync();
        return genre.Id;
    }

    public async Task<int> CountAllMoviesAsync()
    {
        await using var context = NewContext();
        return await context.Movies.CountAsync();
    }

    public async Task<int> CountMoviesAsync(string title)
    {
        await using var context = NewContext();
        return await context.Movies.CountAsync(m => m.Title == title);
    }

    // Много фильмов одним SaveChanges — для тестов, которым нужен объём, а не разные данные. Название одно, годы разные:
    // уникальный индекс (название, год) не пропустил бы одинаковые пары, а одинаковые названия тестам сортировки нужны
    public async Task<List<int>> AddMoviesAsync(string title, int count)
    {
        await using var context = NewContext();
        var movies = Enumerable.Range(0, count).Select(i => new Movie(title, 1000 + i)).ToList();
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

    // Для точного воспроизведения гонки первого PATCH: вставляет запись пары в транзакции, которую пока не завершает.
    // Пока она открыта, другая вставка той же пары (её не видно) встанет в ожидание на уникальном индексе (UserId, MovieId);
    // после CommitAsync ждущий получит нарушение уникальности, ровно как проигравший в настоящей гонке
    public async Task<UncommittedInsert> InsertUserDataWithoutCommitAsync(int userId, int movieId, Status status)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();

        await using var command = new NpgsqlCommand(
            """INSERT INTO "UserMovieData" ("UserId", "MovieId", "Status") VALUES (@userId, @movieId, @status)""", connection, transaction);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("movieId", movieId);
        command.Parameters.AddWithValue("status", (int)status);
        await command.ExecuteNonQueryAsync();

        return new UncommittedInsert(connection, transaction);
    }

    // То же для watchlist: вставляет пару (пользователь, фильм) в незавершённой транзакции; вторая вставка той же пары встанет на уникальном
    // индексе (UserId, MovieId) и после CommitAsync получит нарушение уникальности, ровно как проигравший параллельный PUT
    public async Task<UncommittedInsert> InsertWatchlistItemWithoutCommitAsync(int userId, int movieId)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();

        await using var command = new NpgsqlCommand(
            """INSERT INTO "WatchlistItems" ("UserId", "MovieId", "AddedAt") VALUES (@userId, @movieId, now())""", connection, transaction);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("movieId", movieId);
        await command.ExecuteNonQueryAsync();

        return new UncommittedInsert(connection, transaction);
    }

    // То же для фильма: вставляет фильм (название, год) в незавершённой транзакции; вторая вставка той же пары встанет на уникальном
    // индексе (название, год) и после CommitAsync получит нарушение уникальности
    public async Task<UncommittedInsert> InsertMovieWithoutCommitAsync(string title, int year)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();

        await using var command = new NpgsqlCommand("""INSERT INTO "Movies" ("Title", "Year") VALUES (@title, @year)""", connection, transaction);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("year", year);
        await command.ExecuteNonQueryAsync();

        return new UncommittedInsert(connection, transaction);
    }

    // Ждёт, пока кто-то в базе встанет в ожидание блокировки (проигравший упёрся в незавершённую вставку победителя).
    // Если писатель завершился, так и не встав в ожидание, или ожидание не наступило за 10 секунд, гонка не воспроизвелась
    public async Task WaitUntilWriterIsBlockedAsync(Task writer)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        for (var attempt = 0; attempt < 500 && !writer.IsCompleted; attempt++)
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE NOT granted", connection);

            if ((long)(await command.ExecuteScalarAsync())! > 0)
                return;

            await Task.Delay(20);
        }

        throw new InvalidOperationException("The second writer did not block on the unique index, the race was not reproduced");
    }
}

// Открытая транзакция с вставленной, но не подтверждённой записью. Если её не подтвердили, при освобождении откатывается
public sealed class UncommittedInsert(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
{
    public Task CommitAsync() => transaction.CommitAsync();

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
