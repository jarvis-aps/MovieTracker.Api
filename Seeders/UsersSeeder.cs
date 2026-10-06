using Extensions.Hosting.AsyncInitialization;
using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Seeders;

public class UsersSeeder(MovieTrackerDbContext movieTrackerDbContext) : IAsyncInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!await movieTrackerDbContext.Users.AnyAsync(cancellationToken: cancellationToken))
        {
            var user1 = new User("John Doe");
            var user2 = new User("Rachel Green");
            var user3 = new User("Anton");
            
            movieTrackerDbContext.Users.Add(user1);
            movieTrackerDbContext.Users.Add(user2);
            movieTrackerDbContext.Users.Add(user3);
        }
        
        await movieTrackerDbContext.SaveChangesAsync(cancellationToken);
    }
}