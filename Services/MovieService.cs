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

    public async Task<List<MovieDto>> GetMoviesAsync(MovieQueryParameters movieQueryParameters)
    {
        IQueryable<Movie> query = _context.Movies;
        
        movieQueryParameters.Page ??= 1;
        movieQueryParameters.PageSize ??= 10;
        
        if(movieQueryParameters.Page <= 0 || movieQueryParameters.PageSize <= 0)
        {
            _logger.LogWarning("Page {0} or pageSize {1} not valid", movieQueryParameters.Page, movieQueryParameters.PageSize);
            throw new InvalidPageSizeException("Page and PageSize must be greater than 0");
        }

        if (movieQueryParameters.Status != null)
            query = query.Where(m => m.Status == movieQueryParameters.Status);

        if (movieQueryParameters.Genre != null)
            query = query.Where(m => m.Genres.Contains((Genre)movieQueryParameters.Genre));

        switch (movieQueryParameters.SortBy)
        {
            case SortBy.Title:
                query = movieQueryParameters.SortByDesc is true ? query.OrderByDescending(m => m.Title) : query.OrderBy(m => m.Title);
                break;
            case SortBy.Status:
                query = movieQueryParameters.SortByDesc is true ? query.OrderByDescending(m => m.Status) : query.OrderBy(m => m.Status);
                break;
            case SortBy.Rating:
                query = movieQueryParameters.SortByDesc is true ? query.OrderByDescending(m => m.Rating) : query.OrderBy(m => m.Rating);
                break;
            case SortBy.Genre:
            case null:
                break;
        }

        if (!string.IsNullOrEmpty(movieQueryParameters.SearchPart))
            query = query.Where(m => EF.Functions.ILike(m.Title, $"%{movieQueryParameters.SearchPart}%"));

        query = query.Skip((int)((movieQueryParameters.Page - 1) * movieQueryParameters.PageSize)).Take((int)movieQueryParameters.PageSize);

        List<Movie> movieList = await query.ToListAsync();
        List<MovieDto> moviesDto = new List<MovieDto>(movieList.Count);
        
        moviesDto.AddRange(movieList.Select(movie => new MovieDto(movie.Id, movie.Title, movie.Status,  movie.Genres, movie.Year, movie.Rating, movie.Notes)));
        
        return moviesDto;
    }

    public async Task<MovieDto?> UpdateStatusAsync(int id, Status status)
    {
        var currentMovie = await _context.Movies.FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null)
            return null;

        currentMovie.Status = status;
        await _context.SaveChangesAsync();
        
        var movieDto = new MovieDto(id, currentMovie.Title, currentMovie.Status,  currentMovie.Genres, currentMovie.Year, currentMovie.Rating, currentMovie.Notes);

        return movieDto;
    }
    
    public async Task<MovieDto?> UpdateNotesAsync(int id, string notes)
    {
        var currentMovie = await _context.Movies.FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null)
            return null;

        currentMovie.Notes = notes;
        await _context.SaveChangesAsync();
        
        var movieDto = new MovieDto(id, currentMovie.Title, currentMovie.Status,  currentMovie.Genres, currentMovie.Year, currentMovie.Rating, currentMovie.Notes);

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
        
        var movieDto = new MovieDto(id, currentMovie.Title, currentMovie.Status,  currentMovie.Genres, currentMovie.Year, currentMovie.Rating, currentMovie.Notes);

        return movieDto;
    }

    public async Task<MovieDto?> CreateMovieAsync(NewMovie newMovie)
    {
        var movie = new Movie(newMovie.Title, newMovie.Status, newMovie.Genres, newMovie.Year, newMovie.Notes);

        if (_context.Movies.Any(m => m.Title == newMovie.Title))
        {
            return null;
        }

        await _context.Movies.AddAsync(movie);
        await _context.SaveChangesAsync();

        var movieDto = new MovieDto(movie.Id, movie.Title, movie.Status,  movie.Genres, movie.Year, movie.Rating, movie.Notes);
        
        return movieDto;
    }
} 