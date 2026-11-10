namespace MovieTracker.Api.Mediator;

// Единственное, что знает вызывающий код (эндпоинт): запрос уходит в Send, а кто его обработает, решает mediator
public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request);
}
