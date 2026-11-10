using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MovieTracker.Api.Mediator;

namespace MovieTracker.Api.Tests;

// Mediator без базы: подставные запрос, handler и поведения. Что делает mediator: находит handler по типу запроса,
// пропускает вызов через цепочку поведений и возвращает ответ handler'а
public class MediatorTests
{
    private record Ping(string Text) : IRequest<string>;

    // Все участники пишут в общий журнал, по нему видно порядок вызовов
    private class PingHandler(List<string> journal) : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request)
        {
            journal.Add("handler");
            return Task.FromResult($"pong:{request.Text}");
        }
    }

    private class OuterBehavior<TRequest, TResponse>(List<string> journal) : IPipelineBehavior<TRequest, TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next)
        {
            journal.Add("outer-before");
            var response = await next();
            journal.Add("outer-after");
            return response;
        }
    }

    private class InnerBehavior<TRequest, TResponse>(List<string> journal) : IPipelineBehavior<TRequest, TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next)
        {
            journal.Add("inner-before");
            var response = await next();
            journal.Add("inner-after");
            return response;
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

    private static (IMediator Mediator, List<string> Journal) Build(Action<IServiceCollection>? configure = null)
    {
        var journal = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(journal);
        services.AddScoped<IMediator, DefaultMediator>();
        configure?.Invoke(services);

        return (services.BuildServiceProvider().GetRequiredService<IMediator>(), journal);
    }

    [Fact]
    public async Task Send_CallsHandlerAndReturnsItsResponse()
    {
        var (mediator, journal) = Build(s => s.AddScoped<IRequestHandler<Ping, string>, PingHandler>());

        var response = await mediator.Send(new Ping("hi"));

        Assert.Equal("pong:hi", response);
        Assert.Equal(["handler"], journal);
    }

    // Первое зарегистрированное поведение — самое внешнее: оно начинается раньше всех и заканчивается позже всех
    [Fact]
    public async Task Send_RunsBehaviorsAroundHandler_FirstRegisteredIsOutermost()
    {
        var (mediator, journal) = Build(s =>
        {
            s.AddScoped<IRequestHandler<Ping, string>, PingHandler>();
            s.AddScoped(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>));
            s.AddScoped(typeof(IPipelineBehavior<,>), typeof(InnerBehavior<,>));
        });

        var response = await mediator.Send(new Ping("hi"));

        Assert.Equal("pong:hi", response);
        Assert.Equal(["outer-before", "inner-before", "handler", "inner-after", "outer-after"], journal);
    }

    [Fact]
    public async Task Send_WithoutHandler_FailsWithInvalidOperationException()
    {
        var (mediator, _) = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new Ping("hi")));
    }

    [Fact]
    public async Task TimingBehavior_ReturnsHandlerResponseAndLogsRequestName()
    {
        var logger = new ListLogger<TimingBehavior<Ping, string>>();
        var behavior = new TimingBehavior<Ping, string>(logger);

        var response = await behavior.Handle(new Ping("hi"), () => Task.FromResult("result"));

        Assert.Equal("result", response);
        var message = Assert.Single(logger.Messages);
        Assert.StartsWith("Ping took ", message);
    }

    [Fact]
    public async Task TimingBehavior_WhenHandlerThrows_StillLogsAndPassesExceptionOn()
    {
        var logger = new ListLogger<TimingBehavior<Ping, string>>();
        var behavior = new TimingBehavior<Ping, string>(logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new Ping("hi"), () => throw new InvalidOperationException("boom")));

        Assert.Single(logger.Messages);
    }
}
