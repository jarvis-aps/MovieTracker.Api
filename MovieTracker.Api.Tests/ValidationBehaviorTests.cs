using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MovieTracker.Api.Mediator;

namespace MovieTracker.Api.Tests;

// ValidationBehavior без базы: подставные запрос, handler и валидаторы. Что делает поведение: до handler'а прогоняет все валидаторы
// запроса; есть ошибки — бросает RequestValidationException и handler не вызывается; нет — идёт дальше
public class ValidationBehaviorTests
{
    private record Order(string Name, int Quantity) : IRequest<string>;

    private class OrderHandler(List<string> journal) : IRequestHandler<Order, string>
    {
        public Task<string> Handle(Order request)
        {
            journal.Add("handler");
            return Task.FromResult($"accepted:{request.Name}");
        }
    }

    private class NameValidator : IValidator<Order>
    {
        public IEnumerable<ValidationError> Validate(Order request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                yield return new ValidationError("name", "Name is required");
        }
    }

    // Второй валидатор того же запроса, тоже про поле name: ошибки двух валидаторов складываются
    private class NameLengthValidator : IValidator<Order>
    {
        public IEnumerable<ValidationError> Validate(Order request)
        {
            if (request.Name.Length > 5)
                yield return new ValidationError("name", "Name must be at most 5 characters");
        }
    }

    private class QuantityValidator : IValidator<Order>
    {
        public IEnumerable<ValidationError> Validate(Order request)
        {
            if (request.Quantity <= 0)
                yield return new ValidationError("quantity", "Quantity must be positive");
        }
    }

    private class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    // Как в AddMediator: замер (если нужен) регистрируется раньше проверки, поэтому он внешний
    private static (IMediator Mediator, List<string> Journal) Build(ListLogger<TimingBehavior<Order, string>>? timingLogger, params Type[] validators)
    {
        var journal = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(journal);
        services.AddScoped<IMediator, DefaultMediator>();
        services.AddScoped<IRequestHandler<Order, string>, OrderHandler>();

        if (timingLogger != null)
        {
            services.AddSingleton<ILogger<TimingBehavior<Order, string>>>(timingLogger);
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TimingBehavior<,>));
        }

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        foreach (var validator in validators)
            services.AddScoped(typeof(IValidator<Order>), validator);

        return (services.BuildServiceProvider().GetRequiredService<IMediator>(), journal);
    }

    [Fact]
    public async Task Send_NoValidators_CallsHandler()
    {
        var (mediator, journal) = Build(null);

        var response = await mediator.Send(new Order("", 0));

        Assert.Equal("accepted:", response);
        Assert.Equal(["handler"], journal);
    }

    [Fact]
    public async Task Send_ValidRequest_CallsHandlerAndReturnsItsResponse()
    {
        var (mediator, journal) = Build(null, typeof(NameValidator), typeof(QuantityValidator));

        var response = await mediator.Send(new Order("Alice", 3));

        Assert.Equal("accepted:Alice", response);
        Assert.Equal(["handler"], journal);
    }

    // Главное свойство поведения: при ошибке handler не вызывается, то есть ничего не записывается
    [Fact]
    public async Task Send_InvalidRequest_ThrowsAndDoesNotCallHandler()
    {
        var (mediator, journal) = Build(null, typeof(NameValidator));

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() => mediator.Send(new Order("", 3)));

        Assert.Equal([new ValidationError("name", "Name is required")], exception.Errors);
        Assert.Empty(journal);
    }

    // Все валидаторы запроса работают, и ошибки приходят сразу, а не по одной
    [Fact]
    public async Task Send_SeveralValidators_ReportsAllErrorsAtOnce()
    {
        var (mediator, journal) = Build(null, typeof(NameValidator), typeof(NameLengthValidator), typeof(QuantityValidator));

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() => mediator.Send(new Order("Alexander", 0)));

        Assert.Equal(
            [new ValidationError("name", "Name must be at most 5 characters"), new ValidationError("quantity", "Quantity must be positive")],
            exception.Errors);
        Assert.Empty(journal);
    }

    // Для ValidationProblemDetails ошибки группируются по полю: у одного поля может быть несколько сообщений
    [Fact]
    public void ToErrorsByField_GroupsMessagesOfTheSameField()
    {
        var exception = new RequestValidationException([
            new ValidationError("name", "Name is required"),
            new ValidationError("quantity", "Quantity must be positive"),
            new ValidationError("name", "Name must be at most 5 characters")]);

        var byField = exception.ToErrorsByField();

        Assert.Equal(["Name is required", "Name must be at most 5 characters"], byField["name"]);
        Assert.Equal(["Quantity must be positive"], byField["quantity"]);
        Assert.Equal(2, byField.Count);
    }

    // Замер снаружи проверки: время пишется и для запроса, отклонённого до handler'а
    [Fact]
    public async Task Send_InvalidRequest_IsStillMeasuredByOuterTiming()
    {
        var logger = new ListLogger<TimingBehavior<Order, string>>();
        var (mediator, journal) = Build(logger, typeof(NameValidator));

        await Assert.ThrowsAsync<RequestValidationException>(() => mediator.Send(new Order("", 3)));

        Assert.Contains(logger.Messages, message => message.StartsWith("Order took"));
        Assert.Empty(journal);
    }
}
