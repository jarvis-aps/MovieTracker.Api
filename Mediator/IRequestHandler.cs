namespace MovieTracker.Api.Mediator;

// На каждый тип запроса ровно один handler: он и есть «сценарий» (use case)
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request);
}
