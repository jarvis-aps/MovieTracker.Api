using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;
using Npgsql;

namespace MovieTracker.Api.Services;

public class WatchlistService : IWatchlistReader
{
    private readonly MovieTrackerDbContext _context;
    private readonly ILogger<WatchlistService> _logger;

    public WatchlistService(MovieTrackerDbContext context, ILogger<WatchlistService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // false — фильм или пользователь не найден; true — фильм теперь в списке (в том числе если уже был)
    public async Task<bool> AddAsync(int movieId, int userId)
    {
        if (!await _context.Movies.AnyAsync(m => m.Id == movieId) || !await _context.Users.AnyAsync(u => u.Id == userId))
            return false;

        if (await _context.WatchlistItems.AnyAsync(w => w.UserId == userId && w.MovieId == movieId))
            return true;

        _context.WatchlistItems.Add(new WatchlistItem(userId, movieId));

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Параллельный PUT успел вставить ту же пару между проверкой и SaveChanges — для идемпотентного PUT это успех
            _logger.LogInformation("Movie {0} is already in watchlist of user {1} (concurrent add)", movieId, userId);
        }

        return true;
    }

    // false — фильм или пользователь не найден; true — фильма в списке больше нет (в том числе если его там и не было)
    public async Task<bool> RemoveAsync(int movieId, int userId)
    {
        if (!await _context.Movies.AnyAsync(m => m.Id == movieId) || !await _context.Users.AnyAsync(u => u.Id == userId))
            return false;

        // Один DELETE ... WHERE без загрузки сущности: повторный или параллельный вызов просто удалит 0 строк, а не упадёт
        await _context.WatchlistItems
            .Where(w => w.UserId == userId && w.MovieId == movieId)
            .ExecuteDeleteAsync();

        return true;
    }

    public async Task<HashSet<int>> GetMovieIdsAsync(int userId)
    {
        var movieIds = await _context.WatchlistItems
            .Where(w => w.UserId == userId)
            .Select(w => w.MovieId)
            .ToListAsync();

        return movieIds.ToHashSet();
    }
}
