using Extensions.Hosting.AsyncInitialization;
using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Seeders;

public class WatchlistSeeder(MovieTrackerDbContext movieTrackerDbContext) : IAsyncInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!await movieTrackerDbContext.WatchlistItems.AnyAsync(cancellationToken: cancellationToken))
        {
            var users = await movieTrackerDbContext.Users.OrderBy(u => u.Id).Take(2).ToListAsync(cancellationToken);
            var movies = await movieTrackerDbContext.Movies.OrderBy(m => m.Id).Take(3).ToListAsync(cancellationToken);

            movieTrackerDbContext.WatchlistItems.Add(new WatchlistItem(users[0].Id, movies[0].Id));
            movieTrackerDbContext.WatchlistItems.Add(new WatchlistItem(users[0].Id, movies[2].Id));
            movieTrackerDbContext.WatchlistItems.Add(new WatchlistItem(users[1].Id, movies[2].Id));
        }

        await movieTrackerDbContext.SaveChangesAsync(cancellationToken);
    }
}
