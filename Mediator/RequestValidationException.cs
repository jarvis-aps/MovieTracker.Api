namespace MovieTracker.Api.Mediator;

// Запрос не прошёл проверку: handler не вызывался. Исключение, а не результат, потому что ответ handler'а у каждой операции свой.
// В код ответа его переводит эндпоинт (как InvalidRatingException и другие). Назван так, чтобы не путать с
// System.ComponentModel.DataAnnotations.ValidationException
public class RequestValidationException(IReadOnlyList<ValidationError> errors) : Exception("Request is not valid")
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;

    // Ошибки по полям (одно поле может иметь несколько сообщений), как ждёт ValidationProblemDetails
    public Dictionary<string, string[]> ToErrorsByField() =>
        Errors.GroupBy(error => error.Field).ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
}
