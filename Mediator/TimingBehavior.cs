using System.Diagnostics;

namespace MovieTracker.Api.Mediator;

// Первое сквозное поведение (решение 3.14): пишет в лог, сколько выполнялся запрос. Работает для любой команды и любого запроса,
// ни один handler об этом не знает. finally: время пишется и тогда, когда handler бросил исключение
public class TimingBehavior<TRequest, TResponse>(ILogger<TimingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            return await next();
        }
        finally
        {
            logger.LogInformation("{Request} took {Elapsed} ms", typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);
        }
    }
}
