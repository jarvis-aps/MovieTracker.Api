using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Commands;

// Поставить статус: у этой операции нет своих проверок, поэтому handler только проверяет существование и просит помощника записать.
// null — фильм или пользователь не найден
public class SetStatusHandler(UserMovieDataWriter writer) : IRequestHandler<SetStatusCommand, UserMovieDataDto?>
{
    public async Task<UserMovieDataDto?> Handle(SetStatusCommand command)
    {
        if (!await writer.ExistAsync(command.MovieId, command.UserId))
            return null;

        return await writer.SaveAsync(command.MovieId, command.UserId, data => data.Status = command.Status);
    }
}
