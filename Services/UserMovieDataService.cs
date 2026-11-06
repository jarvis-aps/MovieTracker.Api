using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;

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

        var data = await GetOrCreateAsync(movieId, userId);
        data.Status = status;
        await _context.SaveChangesAsync();

        return ToDto(data);
    }

    public async Task<UserMovieDataDto?> UpdateNotesAsync(int movieId, int userId, string notes)
    {
        if (!await ExistAsync(movieId, userId))
            return null;

        var data = await GetOrCreateAsync(movieId, userId);
        data.Notes = notes;
        await _context.SaveChangesAsync();

        return ToDto(data);
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

        var data = await GetOrCreateAsync(movieId, userId);
        data.Rating = rating;
        await _context.SaveChangesAsync();

        return ToDto(data);
    }

    private async Task<bool> ExistAsync(int movieId, int userId)
    {
        return await _context.Movies.AnyAsync(m => m.Id == movieId) && await _context.Users.AnyAsync(u => u.Id == userId);
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

    private static UserMovieDataDto ToDto(UserMovieData data) => new(data.MovieId, data.Status, data.Rating);
}
