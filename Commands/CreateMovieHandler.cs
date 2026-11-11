using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using Npgsql;

namespace MovieTracker.Api.Commands;

// Логика переехала сюда из MovieService.CreateMovieAsync (решение 3.19). Дубликатом считается пара (название, год), её уникальность
// гарантирует индекс (решение 3.20). Не менялось и осталось известной особенностью: NullReferenceException (500) при отсутствии
// genresId в теле, отсутствие проверки значений (пустое название, любой год)
public class CreateMovieHandler(MovieTrackerDbContext context) : IRequestHandler<CreateMovieCommand, MovieDto?>
{
    // Имя индекса из OnModelCreating (EF называет его IX_{таблица}_{колонки}): по нему наш дубликат отличается от любого другого
    // нарушения уникальности, которое глотать нельзя
    private const string TitleAndYearIndex = "IX_Movies_Title_Year";

    public async Task<MovieDto?> Handle(CreateMovieCommand command)
    {
        var movie = new Movie(command.Title, command.Year);

        var currentGenres = new List<Genre>();

        foreach (var genreId in command.GenresId)
        {
            var currentGenre = await context.Genres.FirstOrDefaultAsync(g => g.Id == genreId);

            if (currentGenre != null)
                currentGenres.Add(currentGenre);
        }

        movie.Genres = currentGenres;

        // Быстрый путь: очевидный дубликат отвечаем без попытки вставки. Гарантию даёт индекс ниже, а не эта проверка:
        // два одновременных запроса с одной парой оба проходят её
        if (await context.Movies.AnyAsync(m => m.Title == command.Title && m.Year == command.Year))
        {
            return null;
        }

        try
        {
            await context.Movies.AddAsync(movie);
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                  {
                                                      SqlState: PostgresErrorCodes.UniqueViolation,
                                                      ConstraintName: TitleAndYearIndex
                                                  })
        {
            // Проигравший в гонке: победитель уже подтвердил фильм с этой парой, и индекс отклонил нашу вставку. Для клиента это тот же дубликат
            return null;
        }

        var genresDto = movie.Genres.Select(genre => new GenreDto(genre.Id, genre.Name)).ToList();
        var movieDto = new MovieDto(movie.Id, movie.Title, genresDto, movie.Year, null, null, false, null);

        return movieDto;
    }
}
