using Extensions.Hosting.AsyncInitialization;
using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Seeders;

public class UserMovieDataSeeder(MovieTrackerDbContext movieTrackerDbContext) : IAsyncInitializer
{

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!await movieTrackerDbContext.UserMovieData.AnyAsync(cancellationToken: cancellationToken))
        {
            var user1 = await movieTrackerDbContext.Users.FirstAsync(cancellationToken);
            var move1 = await movieTrackerDbContext.Movies.FirstAsync(cancellationToken);
            
            var userMovieData1 = new UserMovieData(user1.Id, move1.Id, Status.Watched)
            {
                Rating = 9f,
                Notes = null
            };

            movieTrackerDbContext.UserMovieData.Add(userMovieData1);
            
            var user2 = await movieTrackerDbContext.Users.OrderBy(u => u.Id).Skip(1).FirstAsync(cancellationToken);
            var move2 = await movieTrackerDbContext.Movies.OrderBy(u => u.Id).Skip(1).FirstAsync(cancellationToken);
            
            var userMovieData2 = new UserMovieData(user2.Id, move2.Id, Status.Watched)
            {
                Rating = 4f,
                Notes = "test"
            };

            movieTrackerDbContext.UserMovieData.Add(userMovieData2);
        }
        
        await movieTrackerDbContext.SaveChangesAsync(cancellationToken);
    }
}