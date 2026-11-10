namespace MovieTracker.Api.Mediator;

// Сквозное поведение вокруг handler'а (логирование, валидация...): код до next() выполняется до handler'а, после — после.
// next() вызывает следующее поведение в цепочке или, если поведений больше нет, сам handler
public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next);
}
