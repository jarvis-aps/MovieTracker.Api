using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Initializer;

public class MovieTrackerDbContext : DbContext
{
    public DbSet<Movie> Movies { get; set; }
    public DbSet<Genre> Genres { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserMovieData> UserMovieData { get; set; }

    public MovieTrackerDbContext(DbContextOptions<MovieTrackerDbContext> options) : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserMovieData>()
            .HasIndex(d => new { d.UserId, d.MovieId })
            .IsUnique();
    }
}
