using MovieTracker.Api.Mediator;

namespace MovieTracker.Api.Commands;

// Правила для POST /movies (решение 3.22). Возвращает все ошибки сразу. Имена полей как в JSON (title, year, genresId).
// Неизвестные id жанров здесь не ошибка: handler по-прежнему молча их пропускает
public class CreateMovieValidator(TimeProvider time) : IValidator<CreateMovieCommand>
{
    private const int MaxTitleLength = 200;      // единиц UTF-16 в исходной строке, той, что хранится (эмодзи занимает две)
    private const int FirstFilmYear = 1888;      // первый фильм; пропущенный год приходит как 0 и тоже отклоняется
    private const int YearsAhead = 5;            // допустимы анонсированные фильмы: до текущего года плюс 5
    private const int MaxGenres = 10;

    public IEnumerable<ValidationError> Validate(CreateMovieCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            yield return new ValidationError("title", "Title is required");
        else if (command.Title.Length > MaxTitleLength)
            yield return new ValidationError("title", $"Title must be at most {MaxTitleLength} characters");

        var lastYear = time.GetUtcNow().Year + YearsAhead;
        if (command.Year < FirstFilmYear || command.Year > lastYear)
            yield return new ValidationError("year", $"Year must be between {FirstFilmYear} and {lastYear}");

        if (command.GenresId is null)
            yield return new ValidationError("genresId", "GenresId is required");
        else if (command.GenresId.Count > MaxGenres)
            yield return new ValidationError("genresId", $"GenresId must contain at most {MaxGenres} items");
    }
}
