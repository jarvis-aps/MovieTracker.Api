using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Commands;

// Тонкий handler, как SetStatusHandler: логика остаётся в UserMovieDataService и переедет сюда вместе с остальными двумя командами
public class SetRatingHandler(UserMovieDataService service) : IRequestHandler<SetRatingCommand, UserMovieDataDto?>
{
    public Task<UserMovieDataDto?> Handle(SetRatingCommand command) =>
        service.UpdateRatingAsync(command.MovieId, command.UserId, command.Rating);
}
