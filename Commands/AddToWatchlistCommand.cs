using MovieTracker.Api.Mediator;

namespace MovieTracker.Api.Commands;

// Команда: положить фильм в список «Хочу посмотреть» пользователя. Ответ: false — нет фильма или пользователя (эндпоинт отвечает 404),
// true — фильм теперь в списке, в том числе если уже был (команда идемпотентна)
public record AddToWatchlistCommand(int MovieId, int UserId) : IRequest<bool>;
