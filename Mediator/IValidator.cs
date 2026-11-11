namespace MovieTracker.Api.Mediator;

// Правила проверки одного запроса (команды или запроса чтения). Возвращает все найденные ошибки сразу, пустой результат значит «верно».
// ValidationBehavior вызывает все валидаторы запроса до handler'а; новый валидатор подхватывается сам, как handler (AddMediator)
public interface IValidator<in TRequest>
{
    IEnumerable<ValidationError> Validate(TRequest request);
}

// Одна ошибка: поле (как оно называется в JSON, например genresId) и сообщение
public record ValidationError(string Field, string Message);
