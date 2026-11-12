using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;

namespace MovieTracker.Api.Services;

// Реализация границы Movies ↔ Watchlist: единственное, что остаётся от прежнего WatchlistService после переноса записи в handler'ы
// (решение 3.24, вариант А). Таблицу WatchlistItems по-прежнему читает только watchlist, а каталог видит лишь id фильмов
public class WatchlistReader(MovieTrackerDbContext context) : IWatchlistReader
{
    public async Task<HashSet<int>> GetMovieIdsAsync(int userId)
    {
        var movieIds = await context.WatchlistItems
            .Where(w => w.UserId == userId)
            .Select(w => w.MovieId)
            .ToListAsync();

        return movieIds.ToHashSet();
    }
}
