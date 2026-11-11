using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Seeders;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;
using MovieTracker.Api.Queries;

namespace MovieTracker.Api.Services;

public class MovieService
{
    private readonly MovieTrackerDbContext _context;
    private readonly ILogger<MovieService> _logger;
    private readonly IWatchlistReader _watchlist;

    public MovieService(MovieTrackerDbContext context, ILogger<MovieService> logger, IWatchlistReader watchlist)
    {
        _context = context;
        _logger= logger;
        _watchlist = watchlist;
    }

    // Решение 3.13: у номера страницы нет своей границы, её задаёт число фильмов (totalCount). Страница за последней — пустой
    // список и честный totalCount, без запроса в базу. Смещение считается в long: в int (page - 1) * pageSize переполнялся
    // (214748366 при pageSize 10 давало 500, 429496731 — чужую страницу).
    // Решение 3.15: запрос неизменяем и дефолты page/pageSize лежат в нём самом; здесь значения только читаются
    public async Task<MoviesPage> GetMoviesAsync(GetMoviesQuery request)
    {
        IQueryable<Movie> movies = _context.Movies;

        if (request.Page <= 0 || request.PageSize <= 0)
        {
            _logger.LogWarning("Page {0} or pageSize {1} not valid", request.Page, request.PageSize);
            throw new InvalidPageSizeException("Page and PageSize must be greater than 0");
        }

        // До расчёта Skip: смещение страницы должно считаться от уже урезанного размера. Урезанный размер — результат правила,
        // а не дефолт, и записать его в неизменяемый запрос нельзя, поэтому он единственная локальная переменная
        var pageSize = request.PageSize;
        if (pageSize > GetMoviesQuery.MaxPageSize)
        {
            _logger.LogInformation("PageSize {0} is above the maximum {1}, using {2}", pageSize, GetMoviesQuery.MaxPageSize, GetMoviesQuery.DefaultPageSize);
            pageSize = GetMoviesQuery.DefaultPageSize;
        }

        if (request.GenreId != null)
            movies = movies.Where(m => m.Genres.Any(g => g.Id == request.GenreId));

        if (!string.IsNullOrEmpty(request.SearchPart))
            movies = movies.Where(m => EF.Functions.ILike(m.Title, $"%{request.SearchPart}%"));

        var userId = request.UserId;

        // Watchlist — чужой модуль: его таблицу не читаем, берём только id фильмов и отдаём их в тот же SQL (= ANY(@ids)),
        // поэтому фильтр и флаг по-прежнему считаются в одном запросе, до Skip/Take
        var watchlistMovieIds = await _watchlist.GetMovieIdsAsync(userId);

        // LEFT JOIN на данные пользователя: фильм без записи UserMovieData остаётся в списке как NotWatched без оценки
        var query = movies.Select(m => new
        {
            m.Id,
            m.Title,
            m.Year,
            Genres = m.Genres.Select(genre => new GenreDto(genre.Id, genre.Name)).ToList(),
            Rating = m.MoviesData.Where(d => d.UserId == userId).Select(d => d.Rating).FirstOrDefault(),
            UserStatus = m.MoviesData.Where(d => d.UserId == userId).Select(d => (Status?)d.Status).FirstOrDefault() ?? Status.NotWatched,
            InWatchlist = watchlistMovieIds.Contains(m.Id),
            Notes = m.MoviesData.Where(d => d.UserId == userId).Select(d => d.Notes).FirstOrDefault()
        });

        if (request.Status != null)
            query = query.Where(m => m.UserStatus == request.Status);

        if (request.InWatchlist != null)
            query = query.Where(m => m.InWatchlist == request.InWatchlist);

        // Общее число под теми же фильтрами (статус, жанр, поиск, watchlist), до сортировки и страницы:
        // это то, что клиент листает, а не весь каталог
        var totalCount = await query.CountAsync();

        var offset = (long)(request.Page - 1) * pageSize;

        if (offset >= totalCount)
            return new MoviesPage([], totalCount);

        var desc = request.SortByDesc is true;

        // Без sortBy (и для SortBy.Genre, пока это заглушка) — по названию
        var ordered = request.SortBy switch
        {
            SortBy.Status => (desc ? query.OrderByDescending(m => m.UserStatus) : query.OrderBy(m => m.UserStatus)).ThenBy(m => m.Title),
            // фильмы без оценки всегда в конце: пользователь мог начать смотреть, но ещё не оценить
            SortBy.Rating => (desc ? query.OrderBy(m => m.Rating == null).ThenByDescending(m => m.Rating) : query.OrderBy(m => m.Rating == null).ThenBy(m => m.Rating)).ThenBy(m => m.Title),
            SortBy.Title => desc ? query.OrderByDescending(m => m.Title) : query.OrderBy(m => m.Title),
            _ => query.OrderBy(m => m.Title)
        };

        // внутри группы одинаковых Status/Rating — по названию; Title/Status/Rating не уникальны — без запасного ключа порядок внутри группы одинаковых значений случаен, страницы пересекаются
        query = ordered.ThenBy(m => m.Id);

        // offset меньше totalCount (int), значит в int помещается
        query = query.Skip((int)offset).Take(pageSize);

        var rows = await query.ToListAsync();

        var items = rows.Select(m => new MovieDto(m.Id, m.Title, m.Genres, m.Year, m.Rating, m.UserStatus, m.InWatchlist, m.Notes)).ToList();

        return new MoviesPage(items, totalCount);
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
        var movieDto = new MovieDto(movie.Id, movie.Title, genresDto, movie.Year, null, null, false, null);
        
        return movieDto;
    }
} 