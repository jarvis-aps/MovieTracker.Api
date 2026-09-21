using Extensions.Hosting.AsyncInitialization;
using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Data;

public class GenresSeeder(MovieTrackerDbContext movieTrackerDbContext) : IAsyncInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!await movieTrackerDbContext.Genres.AnyAsync(cancellationToken: cancellationToken))
        {
            var action = new Genre("Action");
            var comedy = new Genre("Comedy");
            var horror = new Genre("Horror");
            var documentary = new Genre("Documentary");
            var sport = new Genre("Sport");

            movieTrackerDbContext.Genres.Add(action);
            movieTrackerDbContext.Genres.Add(comedy);
            movieTrackerDbContext.Genres.Add(horror);
            movieTrackerDbContext.Genres.Add(documentary);
            movieTrackerDbContext.Genres.Add(sport);
        }
        
        await movieTrackerDbContext.SaveChangesAsync(cancellationToken);
    }
}