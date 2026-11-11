using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Commands;

// Записать заметку. Сначала проверка существования (null, то есть 404), потом длина (400): запрос с несуществующим фильмом и слишком
// длинной заметкой отвечает 404, как и раньше
public class SetNotesHandler(UserMovieDataWriter writer, ILogger<SetNotesHandler> logger) : IRequestHandler<SetNotesCommand, UserMovieDataDto?>
{
    // Решение 3.12: заметка не длиннее 500 символов (считаются единицы UTF-16, эмодзи занимает две). Длиннее — ошибка:
    // молча обрезать нельзя, потеряется текст пользователя. Колонка в базе остаётся text, лимит держит только этот код
    private const int MaxNotesLength = 500;

    public async Task<UserMovieDataDto?> Handle(SetNotesCommand command)
    {
        if (!await writer.ExistAsync(command.MovieId, command.UserId))
            return null;

        if (command.Notes.Length > MaxNotesLength)
        {
            logger.LogWarning("For ID {0}, notes length {1} is above {2}", command.MovieId, command.Notes.Length, MaxNotesLength);
            throw new InvalidNotesException($"Notes must be at most {MaxNotesLength} characters");
        }

        return await writer.SaveAsync(command.MovieId, command.UserId, data => data.Notes = command.Notes);
    }
}
