using MovieTracker.Api.Mediator;

namespace MovieTracker.Api.Commands;

// Команда: убрать фильм из списка «Хочу посмотреть» пользователя. Ответ: false — нет фильма или пользователя (эндпоинт отвечает 404),
// true — фильма в списке больше нет, в том числе если его там и не было (команда идемпотентна)
public record RemoveFromWatchlistCommand(int MovieId, int UserId) : IRequest<bool>;
