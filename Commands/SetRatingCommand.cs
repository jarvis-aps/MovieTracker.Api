using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Commands;

// Команда: поставить пользователю оценку фильма. Ответ — личная запись; null, если нет фильма или пользователя.
// Оценка вне диапазона 0–10 — не ответ, а исключение InvalidRatingException (его превращает в 400 эндпоинт)
public record SetRatingCommand(int MovieId, int UserId, float Rating) : IRequest<UserMovieDataDto?>;
