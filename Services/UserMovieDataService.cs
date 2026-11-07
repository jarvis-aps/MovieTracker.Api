using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;
using Npgsql;

namespace MovieTracker.Api.Services;

// Личные данные пользователя о фильме: статус, оценка, заметки. Про Movies и Users знает в одном месте —
// проверка, что фильм и пользователь существуют (как WatchlistService). Ответ — только личная запись,
// поэтому данные каталога и watchlist сервису не нужны
public class UserMovieDataService
{
    private readonly MovieTrackerDbContext _context;
    private readonly ILogger<UserMovieDataService> _logger;

    public UserMovieDataService(MovieTrackerDbContext context, ILogger<UserMovieDataService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Во всех Update*: null — фильм или пользователь не найден

    public async Task<UserMovieDataDto?> UpdateStatusAsync(int movieId, int userId, Status status)
    {
        if (!await ExistAsync(movieId, userId))
            return null;

        return await SaveAsync(movieId, userId, data => data.Status = status);
    }

    public async Task<UserMovieDataDto?> UpdateNotesAsync(int movieId, int userId, string notes)
    {
        if (!await ExistAsync(movieId, userId))
            return null;

        return await SaveAsync(movieId, userId, data => data.Notes = notes);
    }

    public async Task<UserMovieDataDto?> UpdateRatingAsync(int movieId, int userId, float rating)
    {
        if (!await ExistAsync(movieId, userId))
            return null;

        if (rating < 0 || rating > 10)
        {
            _logger.LogWarning("For ID {0}, rating {1} not valid", movieId, rating);
            throw new InvalidRatingException("Rating must be between 0 and 10");
        }

        return await SaveAsync(movieId, userId, data => data.Rating = rating);
    }

    private async Task<bool> ExistAsync(int movieId, int userId)
    {
        return await _context.Movies.AnyAsync(m => m.Id == movieId) && await _context.Users.AnyAsync(u => u.Id == userId);
    }

    // Находит или создаёт запись, применяет изменение и сохраняет. Два одновременных первых PATCH одной пары оба не находят
    // запись и оба вставляют: проигравшего останавливает уникальный индекс (UserId, MovieId)
    private async Task<UserMovieDataDto> SaveAsync(int movieId, int userId, Action<UserMovieData> apply)
    {
        var data = await GetOrCreateAsync(movieId, userId);
        apply(data);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // В отличие от watchlist это нельзя считать успехом: у проигравшего своё изменение (статус, оценка или заметка), и оно ещё не записано.
            // Отсоединяем свою несохранённую вставку, находим запись победителя и применяем то же изменение к ней.
            // Повтор один: индекс срабатывает, только когда победитель уже закоммитил запись, поэтому теперь мы её видим
            _logger.LogInformation("User data of user {0} for movie {1} was created concurrently, applying the change to it", userId, movieId);
            _context.Entry(data).State = EntityState.Detached;

            data = await GetOrCreateAsync(movieId, userId);
            apply(data);
            await _context.SaveChangesAsync();
        }

        return ToDto(data);
    }

    private async Task<UserMovieData> GetOrCreateAsync(int movieId, int userId)
    {
        var data = await _context.UserMovieData.FirstOrDefaultAsync(d => d.MovieId == movieId && d.UserId == userId);

        if (data != null)
            return data;

        data = new UserMovieData(userId, movieId, Status.NotWatched);
        _context.UserMovieData.Add(data);

        return data;
    }

    private static UserMovieDataDto ToDto(UserMovieData data) => new(data.MovieId, data.Status, data.Rating, data.Notes);
}
