using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTracker.Api.Commands;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Tests;

// Общая сборка DI для тестов команд и запросов: настоящий mediator, handler'ы найдены сканированием сборки (AddMediator) и настоящий Postgres.
// Как в приложении: один контекст на запрос (scope), mediator и handler получают его через DI; список watchlist handler запроса
// получает через интерфейс IWatchlistReader, как в Bootstrap.cs
internal static class MediatorTestProvider
{
    public static ServiceProvider Build(TestDatabase db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => db.NewContext());
        services.AddScoped<UserMovieDataWriter>();
        services.AddScoped<WatchlistService>();
        services.AddScoped<IWatchlistReader>(provider => provider.GetRequiredService<WatchlistService>());
        services.AddMediator(typeof(SetStatusHandler).Assembly);

        return services.BuildServiceProvider();
    }
}
