using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Commands;

// Команда: добавить фильм в общий каталог (не в личные данные). Ответ — созданный фильм; null, если фильм с таким названием уже есть.
// Жанры берутся по id, несуществующие id молча пропускаются. Плоская неизменяемая запись, биндится прямо из тела запроса
// (решение 3.19): поля status и notes прежнего класса NewMovie нигде не использовались и убраны, лишние поля в теле игнорируются
public record CreateMovieCommand(string Title, int Year, List<int> GenresId) : IRequest<MovieDto?>;
