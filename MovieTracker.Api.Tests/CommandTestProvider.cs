using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTracker.Api.Commands;
using MovieTracker.Api.Mediator;

namespace MovieTracker.Api.Tests;

// Общая сборка DI для тестов команд: настоящий mediator, handler'ы найдены сканированием сборки (AddMediator) и настоящий Postgres.
// Как в приложении: один контекст на запрос (scope), mediator и handler получают его через DI
internal static class CommandTestProvider
{
    public static ServiceProvider Build(TestDatabase db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped(_ => db.NewContext());
        services.AddScoped<UserMovieDataWriter>();
        services.AddMediator(typeof(SetStatusHandler).Assembly);

        return services.BuildServiceProvider();
    }
}
