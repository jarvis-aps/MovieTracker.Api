namespace MovieTracker.Api.Services;

// Всё, что Movies знает про Watchlist. Таблица WatchlistItems и её устройство остаются внутри Watchlist:
// MovieService видит только id фильмов, поэтому его можно проверить с подставным списком (без таблицы)
public interface IWatchlistReader
{
    // Id фильмов в списке пользователя (пусто — список пуст или пользователя нет)
    Task<HashSet<int>> GetMovieIdsAsync(int userId);
}
