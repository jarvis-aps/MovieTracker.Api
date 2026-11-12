using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Mediator;

namespace MovieTracker.Api.Commands;

// Логика переехала сюда из WatchlistService.RemoveAsync без правок (решение 3.24, вариант А; проверка существования повторена
// намеренно, см. AddToWatchlistHandler)
public class RemoveFromWatchlistHandler(MovieTrackerDbContext context) : IRequestHandler<RemoveFromWatchlistCommand, bool>
{
    public async Task<bool> Handle(RemoveFromWatchlistCommand command)
    {
        if (!await context.Movies.AnyAsync(m => m.Id == command.MovieId) || !await context.Users.AnyAsync(u => u.Id == command.UserId))
            return false;

        // Один DELETE ... WHERE без загрузки сущности: повторный или параллельный вызов просто удалит 0 строк, а не упадёт
        await context.WatchlistItems
            .Where(w => w.UserId == command.UserId && w.MovieId == command.MovieId)
            .ExecuteDeleteAsync();

        return true;
    }
}
