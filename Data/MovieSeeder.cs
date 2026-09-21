using Extensions.Hosting.AsyncInitialization;
using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Data;

public class MovieSeeder(MovieTrackerDbContext movieTrackerDbContext) : IAsyncInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!await movieTrackerDbContext.Movies.AnyAsync(cancellationToken: cancellationToken))
        {
            var genres = await movieTrackerDbContext.Genres.ToDictionaryAsync(g => g.Name, cancellationToken: cancellationToken);
            
            var movie1 = new Movie("Тед Лассо", Status.NotWatched, 2020, null) { Status = Status.NotWatched };
            movie1.Genres = [genres["Comedy"], genres["Sport"]];
            genres["Comedy"].Movies.Add(movie1);
            genres["Sport"].Movies.Add(movie1);
            
            var movie2 = new Movie("Ананасовый экспресс", Status.NotWatched, 2008, null) { Status = Status.NotWatched };
            movie2.Genres = [genres["Horror"], genres["Action"]];
            genres["Horror"].Movies.Add(movie2);
            genres["Action"].Movies.Add(movie2);
            
            var movie3 = new Movie("Пляжный бездельник", Status.NotWatched, 2019, "example") { Status = Status.NotWatched };
            movie3.Genres = [genres["Comedy"], genres["Documentary"]];
            genres["Comedy"].Movies.Add(movie3);
            genres["Documentary"].Movies.Add(movie3);
            
            movieTrackerDbContext.Movies.Add(movie1);
            movieTrackerDbContext.Movies.Add(movie2);
            movieTrackerDbContext.Movies.Add(movie3);
        }

        await movieTrackerDbContext.Database.MigrateAsync(cancellationToken);
        await movieTrackerDbContext.SaveChangesAsync(cancellationToken);
    }
}