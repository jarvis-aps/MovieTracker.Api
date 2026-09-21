using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Data;

public class MovieTrackerDbContext : DbContext
{
    public DbSet<Movie> Movies { get; set; }
    public DbSet<Genre> Genres { get; set; }

    public MovieTrackerDbContext(DbContextOptions<MovieTrackerDbContext> options) : base(options)
    {

    }
}
