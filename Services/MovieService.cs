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
        IQueryable<Movie> query = _context.Movies.Include(m => m.Genres);
        
        movieQueryParameters.Page ??= 1;
        movieQueryParameters.PageSize ??= 10;
        
        if(movieQueryParameters.Page <= 0 || movieQueryParameters.PageSize <= 0)
        {
            _logger.LogWarning("Page {0} or pageSize {1} not valid", movieQueryParameters.Page, movieQueryParameters.PageSize);
            throw new InvalidPageSizeException("Page and PageSize must be greater than 0");
        }

        if (movieQueryParameters.Status != null)
            query = query.Where(m => m.Status == movieQueryParameters.Status);

        if (movieQueryParameters.GenreId != null)
            query = query.Where(m => m.Genres.Any(g => g.Id == movieQueryParameters.GenreId));

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
        
        moviesDto.AddRange(movieList.Select(movie => new MovieDto(movie.Id, movie.Title, movie.Status, movie.Genres.Select(genre => new GenreDto(genre.Id, genre.Name)).ToList(), movie.Year, movie.Rating, movie.Notes)));
        
        return moviesDto;
    }

    public async Task<MovieDto?> UpdateStatusAsync(int id, Status status)
    {
        var currentMovie = await _context.Movies.Include(m => m.Genres).FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null)
            return null;

        currentMovie.Status = status;
        await _context.SaveChangesAsync();

        var genresDto = currentMovie.Genres.Select(genre => new GenreDto(genre.Id, genre.Name)).ToList();
        var movieDto = new MovieDto(id, currentMovie.Title, currentMovie.Status, genresDto, currentMovie.Year, currentMovie.Rating, currentMovie.Notes);
        
        return movieDto;
    }
    
    public async Task<MovieDto?> UpdateNotesAsync(int id, string notes)
    {
        var currentMovie = await _context.Movies.Include(m => m.Genres).FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null)
            return null;

        currentMovie.Notes = notes;
        await _context.SaveChangesAsync();

        var genresDto = currentMovie.Genres.Select(genre => new GenreDto(genre.Id, genre.Name)).ToList();
        var movieDto = new MovieDto(id, currentMovie.Title, currentMovie.Status, genresDto, currentMovie.Year, currentMovie.Rating, currentMovie.Notes);

        return movieDto;
    }
    
    public async Task<MovieDto?> UpdateRatingAsync(int id, float rating)
    {
        var currentMovie = await _context.Movies.Include(m => m.Genres).FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null)
            return null;

        if (rating < 0 || rating > 10)
        {
            _logger.LogWarning("For ID {0}, rating {1} not valid", id, rating);
            throw new InvalidRatingException("Rating must be between 0 and 10");
        }

        currentMovie.Rating = rating;
        await _context.SaveChangesAsync();

        var genresDto = currentMovie.Genres.Select(genre => new GenreDto(genre.Id, genre.Name)).ToList();
        var movieDto = new MovieDto(id, currentMovie.Title, currentMovie.Status, genresDto, currentMovie.Year, currentMovie.Rating, currentMovie.Notes);

        return movieDto;
    }

    public async Task<MovieDto?> CreateMovieAsync(NewMovie newMovie)
    {
        var movie = new Movie(newMovie.Title, newMovie.Status, newMovie.Year, newMovie.Notes);

        var currentGenres = new List<Genre>();
        
        foreach (var genreId in newMovie.GenresId)
        {
            var currentGenre = await _context.Genres.FirstOrDefaultAsync(g => g.Id == genreId);
            
            if(currentGenre != null)
                currentGenres.Add(currentGenre);
        }
        
        movie.Genres = currentGenres;
        
        if (_context.Movies.Any(m => m.Title == newMovie.Title))
        {
            return null;
        }

        await _context.Movies.AddAsync(movie);
        await _context.SaveChangesAsync();

        var genresDto = movie.Genres.Select(genre => new GenreDto(genre.Id, genre.Name)).ToList();
        var movieDto = new MovieDto(movie.Id, movie.Title, movie.Status, genresDto, movie.Year, movie.Rating, movie.Notes);
        
        return movieDto;
    }
} 