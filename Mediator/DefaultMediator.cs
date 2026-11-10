namespace MovieTracker.Api.Mediator;

// Достаёт handler запроса из DI и вызывает его через цепочку поведений (IPipelineBehavior).
// Тип запроса известен только во время выполнения, а handler ищется по закрытому дженерику, поэтому вызов идёт
// через обёртку RequestHandlerWrapper<TRequest, TResponse>: так обходятся без dynamic и проверок типов
public class DefaultMediator(IServiceProvider services) : IMediator
{
    // async: любая ошибка (в том числе «handler не зарегистрирован») приходит как упавшая задача, а не как исключение до await
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
    {
        var wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(request.GetType(), typeof(TResponse));
        var wrapper = (RequestHandlerWrapper<TResponse>)Activator.CreateInstance(wrapperType)!;

        return await wrapper.Handle(request, services);
    }
}

internal abstract class RequestHandlerWrapper<TResponse>
{
    public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider services);
}

internal class RequestHandlerWrapper<TRequest, TResponse> : RequestHandlerWrapper<TResponse> where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider services)
    {
        var typedRequest = (TRequest)request;
        var handler = services.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        Func<Task<TResponse>> next = () => handler.Handle(typedRequest);

        // Первое зарегистрированное поведение — самое внешнее, поэтому оборачиваем с конца списка
        foreach (var behavior in services.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
        {
            var inner = next;
            next = () => behavior.Handle(typedRequest, inner);
        }

        return next();
    }
}
