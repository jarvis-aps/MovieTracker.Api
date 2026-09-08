using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Data;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Services;

public class MovieService
{
    private readonly MovieTrackerDbContext _context;
    private readonly ILogger<MovieService> _logger;

    public MovieService(MovieTrackerDbContext context, ILogger<MovieService> logger)
    {
        _context = context;
        _logger= logger;
    }

    public async Task<List<MovieDto>> GetMoviesAsync(Status? status, Genre? genre, SortBy? sortBy, bool? sortByDesc)
    {
        IQueryable<Movie> query = _context.Movies;

        if (status != null)
            query = query.Where(m => m.Status == status);

        if (genre != null)
            query = query.Where(m => m.Genres.Contains((Genre)genre));

        switch (sortBy)
        {
            case SortBy.Title:
                query = sortByDesc is true ? query.OrderByDescending(m => m.Title) : query.OrderBy(m => m.Title);
                break;
            case SortBy.Status:
                query = sortByDesc is true ? query.OrderByDescending(m => m.Status) : query.OrderBy(m => m.Status);
                break;
            case SortBy.Rating:
                query = sortByDesc is true ? query.OrderByDescending(m => m.Rating) : query.OrderBy(m => m.Rating);
                break;
            case SortBy.Genre:
            case null:
                break;
        }

        List<Movie> movieList = await query.ToListAsync();
        List<MovieDto> moviesDto = new List<MovieDto>(movieList.Count);
        
        moviesDto.AddRange(movieList.Select(movie => new MovieDto(movie.Id, movie.Title, movie.Status,  movie.Genres, movie.Year, movie.Rating)));
        
        return moviesDto;
    }

    public async Task<MovieDto?> UpdateStatusAsync(int id, Status status)
    {
        var currentMovie = await _context.Movies.FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null)
            return null;

        currentMovie.Status = status;
        await _context.SaveChangesAsync();
        
        var movieDto = new MovieDto(id, currentMovie.Title, currentMovie.Status,  currentMovie.Genres, currentMovie.Year, currentMovie.Rating);

        return movieDto;
    }
    
    public async Task<MovieDto?> UpdateRatingAsync(int id, float rating)
    {
        var currentMovie = await _context.Movies.FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null)
            return null;

        if (rating < 0 || rating > 10)
        {
            _logger.LogWarning("For ID {0}, rating {1} not valid", id, rating);
            throw new InvalidRatingException("Rating must be between 0 and 10");
        }

        currentMovie.Rating = rating;
        await _context.SaveChangesAsync();
        
        var movieDto = new MovieDto(id, currentMovie.Title, currentMovie.Status,  currentMovie.Genres, currentMovie.Year, currentMovie.Rating);

        return movieDto;
    }

    public async Task<MovieDto?> CreateMovieAsync(NewMovie newMovie)
    {
        var movie = new Movie(newMovie.Title, newMovie.Status, newMovie.Genres, newMovie.Year);

        if (_context.Movies.Any(m => m.Title == newMovie.Title))
        {
            return null;
        }

        await _context.Movies.AddAsync(movie);
        await _context.SaveChangesAsync();

        var movieDto = new MovieDto(movie.Id, movie.Title, movie.Status,  movie.Genres, movie.Year, movie.Rating);
        
        return movieDto;
    }
} 