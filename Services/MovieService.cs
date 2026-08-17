using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Data;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Services;

public class MovieService
{
    private readonly MovieTrackerDbContext _context;

    public MovieService(MovieTrackerDbContext context)
    {
        _context = context;
    }

    public async Task<List<MovieDto>> GetMoviesAsync()
    {
        List<Movie> movieList = await _context.Movies.ToListAsync();
        List<MovieDto> moviesDto = new List<MovieDto>(movieList.Count);
        moviesDto.AddRange(movieList.Select(movie => new MovieDto(movie.Id, movie.Title, movie.Status)));

        return moviesDto;
    }

    public async Task<MovieDto?> UpdateStatusAsync(int id, Status status)
    {
        var currentMovie = await _context.Movies.FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null)
            return null;

        currentMovie.Status = status;
        await _context.SaveChangesAsync();
        
        var movieDto = new MovieDto(id, currentMovie.Title, currentMovie.Status);

        return movieDto;
    }

    public async Task<MovieDto?> CreateMovieAsync(NewMovie newMovie)
    {
        var movie = new Movie(newMovie.Title, newMovie.Status);

        if (_context.Movies.Any(m => m.Title == newMovie.Title))
        {
            return null;
        }

        await _context.Movies.AddAsync(movie);
        await _context.SaveChangesAsync();

        var movieDto = new MovieDto(movie.Id, movie.Title, movie.Status);
        
        return movieDto;
    }
} 