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
            var movie1 = new Movie("Тед Лассо", Status.NotWatched, [Genre.Comedy, Genre.Sport], 2020, null) { Status = Status.NotWatched };
            var movie2 = new Movie("Ананасовый экспресс", Status.NotWatched, [Genre.Action, Genre.Comedy], 2008, null) { Status = Status.NotWatched };
            var movie3 = new Movie("Пляжный бездельник", Status.NotWatched, [Genre.Action, Genre.Comedy], 2019, "example") { Status = Status.NotWatched };
            movieTrackerDbContext.Movies.Add(movie1);
            movieTrackerDbContext.Movies.Add(movie2);
            movieTrackerDbContext.Movies.Add(movie3);
        }

        await movieTrackerDbContext.Database.MigrateAsync(cancellationToken);
        await movieTrackerDbContext.SaveChangesAsync(cancellationToken);
    }
}