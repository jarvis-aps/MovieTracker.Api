using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Commands;

// Логика переехала сюда из MovieService.CreateMovieAsync без правок поведения (решение 3.19), в том числе то, что замер показал
// и пока не менялось: проверка дубликата названия «спросить, потом вставить» (на Title нет уникального индекса, два одновременных
// запроса с одним названием могут оба пройти), синхронный Any, NullReferenceException (500) при отсутствии genresId в теле
public class CreateMovieHandler(MovieTrackerDbContext context) : IRequestHandler<CreateMovieCommand, MovieDto?>
{
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

        if (context.Movies.Any(m => m.Title == command.Title))
        {
            return null;
        }

        await context.Movies.AddAsync(movie);
        await context.SaveChangesAsync();

        var genresDto = movie.Genres.Select(genre => new GenreDto(genre.Id, genre.Name)).ToList();
        var movieDto = new MovieDto(movie.Id, movie.Title, genresDto, movie.Year, null, null, false, null);

        return movieDto;
    }
}
