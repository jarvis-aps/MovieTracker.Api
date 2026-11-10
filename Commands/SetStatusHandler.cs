using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Commands;

// Тонкий handler (решение 3.14): логика остаётся в UserMovieDataService. Она переедет сюда, когда командами станут
// все три операции (статус, оценка, заметка): общие части SaveAsync, ExistAsync и GetOrCreateAsync нужны всем трём
public class SetStatusHandler(UserMovieDataService service) : IRequestHandler<SetStatusCommand, UserMovieDataDto?>
{
    public Task<UserMovieDataDto?> Handle(SetStatusCommand command) =>
        service.UpdateStatusAsync(command.MovieId, command.UserId, command.Status);
}
