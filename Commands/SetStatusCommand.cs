using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Commands;

// Команда: поставить пользователю статус фильма. Ответ — личная запись; null, если нет фильма или пользователя
public record SetStatusCommand(int MovieId, int UserId, Status Status) : IRequest<UserMovieDataDto?>;
