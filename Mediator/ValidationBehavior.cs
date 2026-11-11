namespace MovieTracker.Api.Mediator;

// Второе сквозное поведение (решение 3.22): до handler'а прогоняет все валидаторы запроса. Нашлись ошибки — бросает
// RequestValidationException, и handler не вызывается; нет валидаторов или нет ошибок — идёт дальше по цепочке.
// Стоит внутри TimingBehavior (регистрируется позже), поэтому замер пишет время и для отклонённых запросов
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
{
    public Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next)
    {
        var errors = validators.SelectMany(validator => validator.Validate(request)).ToList();

        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        return next();
    }
}
