using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Queries;

// Запрос: страница каталога в личном виде пользователя UserId. Ничего не меняет, в ответ — страница и общее число фильмов под фильтрами.
// Запись неизменяема (решение 3.15): сервис свои значения в неё не пишет. Дефолты страницы лежат здесь, в конструкторе: если клиент
// не прислал page или pageSize, [AsParameters] подставит 1 и 100. Ошибки ввода (page=abc, число больше int) ловит биндинг до запроса
public record GetMoviesQuery(
    int UserId,
    Status? Status = null,
    int? GenreId = null,
    SortBy? SortBy = null,
    bool? SortByDesc = null,
    int Page = 1,
    int PageSize = GetMoviesQuery.DefaultPageSize,
    string? SearchPart = null,
    bool? InWatchlist = null) : IRequest<MoviesPage>
{
    // Решение 3.12 (числа пересмотрены в 3.13): за один запрос больше MaxPageSize фильмов не отдаём, иначе ответ (с заметками)
    // растёт без границы. Если клиент просит больше, ошибки нет: страница будет размера по умолчанию.
    // Одно место для обоих чисел: ими пользуются и дефолт записи, и сервис
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 100;
}
