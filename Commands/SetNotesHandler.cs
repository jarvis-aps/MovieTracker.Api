using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Commands;

// Тонкий handler, как SetStatusHandler: логика остаётся в UserMovieDataService и переедет сюда вместе с остальными двумя командами
public class SetNotesHandler(UserMovieDataService service) : IRequestHandler<SetNotesCommand, UserMovieDataDto?>
{
    public Task<UserMovieDataDto?> Handle(SetNotesCommand command) =>
        service.UpdateNotesAsync(command.MovieId, command.UserId, command.Notes);
}
