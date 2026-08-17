using Microsoft.EntityFrameworkCore;

namespace MovieTracker.Api;

public class MovieTrackerDbContext : DbContext
{
    public DbSet<Movie> Movies { get; set; }
    
    public MovieTrackerDbContext(DbContextOptions<MovieTrackerDbContext> options) : base(options)
    {
      
    }
}