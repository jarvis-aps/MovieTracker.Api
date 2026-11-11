using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Initializer;

public class MovieTrackerDbContext : DbContext
{
    public DbSet<Movie> Movies { get; set; }
    public DbSet<Genre> Genres { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserMovieData> UserMovieData { get; set; }
    public DbSet<WatchlistItem> WatchlistItems { get; set; }

    public MovieTrackerDbContext(DbContextOptions<MovieTrackerDbContext> options) : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Решение 3.20: фильм уникален по паре (название, год), ремейки с тем же названием допустимы. Гарантию даёт индекс,
        // а не проверка в коде: два одновременных POST с одной парой иначе оба проходили проверку и создавали дубликат
        modelBuilder.Entity<Movie>()
            .HasIndex(m => new { m.Title, m.Year })
            .IsUnique();

        modelBuilder.Entity<UserMovieData>()
            .HasIndex(d => new { d.UserId, d.MovieId })
            .IsUnique();

        modelBuilder.Entity<WatchlistItem>()
            .HasIndex(w => new { w.UserId, w.MovieId })
            .IsUnique();
    }
}
