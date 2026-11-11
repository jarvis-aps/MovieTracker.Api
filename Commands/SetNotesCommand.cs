using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Commands;

// Команда: записать пользователю заметку к фильму, заменив прежнюю. Ответ — личная запись; null, если нет фильма или пользователя.
// Слишком длинная заметка — не ответ, а исключение InvalidNotesException (его превращает в 400 эндпоинт)
public record SetNotesCommand(int MovieId, int UserId, string Notes) : IRequest<UserMovieDataDto?>;
