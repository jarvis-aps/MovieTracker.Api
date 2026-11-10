using System.Reflection;

namespace MovieTracker.Api.Mediator;

public static class MediatorExtensions
{
    // Регистрирует mediator, все handler'ы из сборки и сквозные поведения. Порядок регистрации поведений — порядок цепочки:
    // первое — самое внешнее. Новый handler подхватывается сам (достаточно реализовать IRequestHandler), строку в Bootstrap добавлять не надо
    public static IServiceCollection AddMediator(this IServiceCollection services, Assembly handlersAssembly)
    {
        services.AddScoped<IMediator, DefaultMediator>();

        var registrations = handlersAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(i => (Service: i, Implementation: type)));

        foreach (var (service, implementation) in registrations)
            services.AddScoped(service, implementation);

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TimingBehavior<,>));

        return services;
    }
}
