using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Seeders;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Initializer;
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
        IQueryable<Movie> movies = _context.Movies;
        
        movieQueryParameters.Page ??= 1;
        movieQueryParameters.PageSize ??= 10;
        
        if(movieQueryParameters.Page <= 0 || movieQueryParameters.PageSize <= 0)
        {
            _logger.LogWarning("Page {0} or pageSize {1} not valid", movieQueryParameters.Page, movieQueryParameters.PageSize);
            throw new InvalidPageSizeException("Page and PageSize must be greater than 0");
        }

        if (movieQueryParameters.GenreId != null)
            movies = movies.Where(m => m.Genres.Any(g => g.Id == movieQueryParameters.GenreId));

        if (!string.IsNullOrEmpty(movieQueryParameters.SearchPart))
            movies = movies.Where(m => EF.Functions.ILike(m.Title, $"%{movieQueryParameters.SearchPart}%"));

        var userId = movieQueryParameters.UserId;

        // LEFT JOIN на данные пользователя: фильм без записи UserMovieData остаётся в списке как NotWatched без оценки
        var query = movies.Select(m => new
        {
            m.Id,
            m.Title,
            m.Year,
            Genres = m.Genres.Select(genre => new GenreDto(genre.Id, genre.Name)).ToList(),
            Rating = m.MoviesData.Where(d => d.UserId == userId).Select(d => d.Rating).FirstOrDefault(),
            UserStatus = m.MoviesData.Where(d => d.UserId == userId).Select(d => (Status?)d.Status).FirstOrDefault() ?? Status.NotWatched
        });

        if (movieQueryParameters.Status != null)
            query = query.Where(m => m.UserStatus == movieQueryParameters.Status);

        var desc = movieQueryParameters.SortByDesc is true;

        // Без sortBy (и для SortBy.Genre, пока это заглушка) — по названию
        var ordered = movieQueryParameters.SortBy switch
        {
            SortBy.Status => (desc ? query.OrderByDescending(m => m.UserStatus) : query.OrderBy(m => m.UserStatus)).ThenBy(m => m.Title),
            // фильмы без оценки всегда в конце: пользователь мог начать смотреть, но ещё не оценить
            SortBy.Rating => (desc ? query.OrderBy(m => m.Rating == null).ThenByDescending(m => m.Rating) : query.OrderBy(m => m.Rating == null).ThenBy(m => m.Rating)).ThenBy(m => m.Title),
            SortBy.Title => desc ? query.OrderByDescending(m => m.Title) : query.OrderBy(m => m.Title),
            _ => query.OrderBy(m => m.Title)
        };

        // внутри группы одинаковых Status/Rating — по названию; Title/Status/Rating не уникальны — без запасного ключа порядок внутри группы одинаковых значений случаен, страницы пересекаются
        query = ordered.ThenBy(m => m.Id);

        query = query.Skip((int)((movieQueryParameters.Page - 1) * movieQueryParameters.PageSize)).Take((int)movieQueryParameters.PageSize);

        var rows = await query.ToListAsync();

        return rows.Select(m => new MovieDto(m.Id, m.Title, m.Genres, m.Year, m.Rating, m.UserStatus)).ToList();
    }

    public async Task<MovieDto?> UpdateStatusAsync(int id, int userId, Status status)
    {
        var currentMovie = await _context.Movies.Include(m => m.Genres).FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null || !await _context.Users.AnyAsync(u => u.Id == userId))
            return null;

        var data = await GetOrCreateUserDataAsync(id, userId);
        data.Status = status;
        await _context.SaveChangesAsync();

        return ToDto(currentMovie, data);
    }
    
    public async Task<MovieDto?> UpdateNotesAsync(int id, int userId, string notes)
    {
        var currentMovie = await _context.Movies.Include(m => m.Genres).FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null || !await _context.Users.AnyAsync(u => u.Id == userId))
            return null;

        var data = await GetOrCreateUserDataAsync(id, userId);
        data.Notes = notes;
        await _context.SaveChangesAsync();

        return ToDto(currentMovie, data);
    }
    
    public async Task<MovieDto?> UpdateRatingAsync(int id, int userId, float rating)
    {
        var currentMovie = await _context.Movies.Include(m => m.Genres).FirstOrDefaultAsync(m => m.Id == id);

        if (currentMovie == null || !await _context.Users.AnyAsync(u => u.Id == userId))
            return null;

        if (rating < 0 || rating > 10)
        {
            _logger.LogWarning("For ID {0}, rating {1} not valid", id, rating);
            throw new InvalidRatingException("Rating must be between 0 and 10");
        }

        var data = await GetOrCreateUserDataAsync(id, userId);
        data.Rating = rating;
        await _context.SaveChangesAsync();

        return ToDto(currentMovie, data);
    }

    private async Task<UserMovieData> GetOrCreateUserDataAsync(int movieId, int userId)
    {
        var data = await _context.UserMovieData.FirstOrDefaultAsync(d => d.MovieId == movieId && d.UserId == userId);

        if (data != null)
            return data;

        data = new UserMovieData(userId, movieId, Status.NotWatched);
        _context.UserMovieData.Add(data);

        return data;
    }

    private static MovieDto ToDto(Movie movie, UserMovieData data)
    {
        var genresDto = movie.Genres.Select(genre => new GenreDto(genre.Id, genre.Name)).ToList();

        return new MovieDto(movie.Id, movie.Title, genresDto, movie.Year, data.Rating, data.Status);
    }

    public async Task<MovieDto?> CreateMovieAsync(NewMovie newMovie)
    {
        var movie = new Movie(newMovie.Title, newMovie.Year);

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
        var movieDto = new MovieDto(movie.Id, movie.Title, genresDto, movie.Year, null, null);
        
        return movieDto;
    }
} 