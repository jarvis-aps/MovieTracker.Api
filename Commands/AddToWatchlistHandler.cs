using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using Npgsql;

namespace MovieTracker.Api.Commands;

// Логика переехала сюда из WatchlistService.AddAsync без правок (решение 3.24, вариант А: целиком в handler'ах, сервис записи удалён).
// Проверка «фильм и пользователь существуют» повторена в RemoveFromWatchlistHandler намеренно: общего у двух операций только эти две строки,
// помощник ради них не нужен, а переиспользовать UserMovieDataWriter.ExistAsync нельзя без зависимости watchlist от класса чужого модуля
public class AddToWatchlistHandler(MovieTrackerDbContext context, ILogger<AddToWatchlistHandler> logger) : IRequestHandler<AddToWatchlistCommand, bool>
{
    public async Task<bool> Handle(AddToWatchlistCommand command)
    {
        if (!await context.Movies.AnyAsync(m => m.Id == command.MovieId) || !await context.Users.AnyAsync(u => u.Id == command.UserId))
            return false;

        if (await context.WatchlistItems.AnyAsync(w => w.UserId == command.UserId && w.MovieId == command.MovieId))
            return true;

        context.WatchlistItems.Add(new WatchlistItem(command.UserId, command.MovieId));

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Параллельный PUT успел вставить ту же пару между проверкой и SaveChanges — для идемпотентного PUT это успех
            logger.LogInformation("Movie {0} is already in watchlist of user {1} (concurrent add)", command.MovieId, command.UserId);
        }

        return true;
    }
}
