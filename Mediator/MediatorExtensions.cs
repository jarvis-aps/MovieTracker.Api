using System.Reflection;

namespace MovieTracker.Api.Mediator;

public static class MediatorExtensions
{
    // Регистрирует mediator, все handler'ы и валидаторы из сборки и сквозные поведения. Порядок регистрации поведений — порядок цепочки:
    // первое — самое внешнее (замер снаружи, проверка внутри). Новый handler и новый валидатор подхватываются сами (достаточно
    // реализовать IRequestHandler или IValidator), строку в Bootstrap добавлять не надо
    public static IServiceCollection AddMediator(this IServiceCollection services, Assembly handlersAssembly)
    {
        services.AddScoped<IMediator, DefaultMediator>();

        AddImplementations(services, handlersAssembly, typeof(IRequestHandler<,>));
        AddImplementations(services, handlersAssembly, typeof(IValidator<>));

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TimingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }

    // Каждый неабстрактный класс сборки, реализующий закрытый вариант открытого интерфейса, регистрируется под этим интерфейсом (scoped:
    // handler'ам и валидаторам нужен DbContext запроса или время)
    private static void AddImplementations(IServiceCollection services, Assembly assembly, Type openInterface)
    {
        var registrations = assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface)
                .Select(i => (Service: i, Implementation: type)));

        foreach (var (service, implementation) in registrations)
            services.AddScoped(service, implementation);
    }
}
