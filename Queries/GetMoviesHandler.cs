using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Queries;

// Тонкий handler (решение 3.15), как SetStatusHandler в 3.14: логика остаётся в MovieService.GetMoviesAsync.
// Перенос логики запроса сюда — отдельной задачей; тогда MovieService перестанет знать про GetMoviesQuery, а в нём останется CreateMovieAsync
public class GetMoviesHandler(MovieService service) : IRequestHandler<GetMoviesQuery, MoviesPage>
{
    public Task<MoviesPage> Handle(GetMoviesQuery query) => service.GetMoviesAsync(query);
}
