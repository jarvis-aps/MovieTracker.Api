using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Commands;

// Поставить оценку. Сначала проверка существования (null, то есть 404), потом диапазон (400): запрос с несуществующим фильмом и неверной
// оценкой отвечает 404, как и раньше
public class SetRatingHandler(UserMovieDataWriter writer, ILogger<SetRatingHandler> logger) : IRequestHandler<SetRatingCommand, UserMovieDataDto?>
{
    public async Task<UserMovieDataDto?> Handle(SetRatingCommand command)
    {
        if (!await writer.ExistAsync(command.MovieId, command.UserId))
            return null;

        // NaN отдельно: любое сравнение с NaN ложно, поэтому «< 0 || > 10» его пропускало. Оно сохранялось в базу (real принимает NaN),
        // клиент получал 500 (JSON не умеет NaN), а GET /movies этого пользователя отдавал 500, пока оценку не перезапишут
        if (float.IsNaN(command.Rating) || command.Rating < 0 || command.Rating > 10)
        {
            logger.LogWarning("For ID {0}, rating {1} not valid", command.MovieId, command.Rating);
            throw new InvalidRatingException("Rating must be between 0 and 10");
        }

        return await writer.SaveAsync(command.MovieId, command.UserId, data => data.Rating = command.Rating);
    }
}
