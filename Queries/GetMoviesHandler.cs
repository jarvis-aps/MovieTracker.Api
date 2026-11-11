using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Queries;

// Страница каталога в личном виде пользователя. Логика переехала сюда из MovieService.GetMoviesAsync без правок (решение 3.19):
// тот же SQL, те же три запроса к базе, те же проверки
public class GetMoviesHandler(MovieTrackerDbContext context, ILogger<GetMoviesHandler> logger, IWatchlistReader watchlist)
    : IRequestHandler<GetMoviesQuery, MoviesPage>
{
    // Решение 3.13: у номера страницы нет своей границы, её задаёт число фильмов (totalCount). Страница за последней — пустой
    // список и честный totalCount, без запроса в базу. Смещение считается в long: в int (page - 1) * pageSize переполнялся
    // (214748366 при pageSize 10 давало 500, 429496731 — чужую страницу).
    // Решение 3.15: запрос неизменяем и дефолты page/pageSize лежат в нём самом; здесь значения только читаются
    public async Task<MoviesPage> Handle(GetMoviesQuery request)
    {
        IQueryable<Movie> movies = context.Movies;

        if (request.Page <= 0 || request.PageSize <= 0)
        {
            logger.LogWarning("Page {0} or pageSize {1} not valid", request.Page, request.PageSize);
            throw new InvalidPageSizeException("Page and PageSize must be greater than 0");
        }

        // До расчёта Skip: смещение страницы должно считаться от уже урезанного размера. Урезанный размер — результат правила,
        // а не дефолт, и записать его в неизменяемый запрос нельзя, поэтому он единственная локальная переменная
        var pageSize = request.PageSize;
        if (pageSize > GetMoviesQuery.MaxPageSize)
        {
            logger.LogInformation("PageSize {0} is above the maximum {1}, using {2}", pageSize, GetMoviesQuery.MaxPageSize, GetMoviesQuery.DefaultPageSize);
            pageSize = GetMoviesQuery.DefaultPageSize;
        }

        if (request.GenreId != null)
            movies = movies.Where(m => m.Genres.Any(g => g.Id == request.GenreId));

        if (!string.IsNullOrEmpty(request.SearchPart))
            movies = movies.Where(m => EF.Functions.ILike(m.Title, $"%{request.SearchPart}%"));

        var userId = request.UserId;

        // Watchlist — чужой модуль: его таблицу не читаем, берём только id фильмов и отдаём их в тот же SQL (= ANY(@ids)),
        // поэтому фильтр и флаг по-прежнему считаются в одном запросе, до Skip/Take
        var watchlistMovieIds = await watchlist.GetMovieIdsAsync(userId);

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
}
