using Extensions.Hosting.AsyncInitialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MovieTracker.Api.Initializer;

namespace MovieTracker.Api.Tests;

// Настоящее приложение (тот же Bootstrap.cs, те же эндпоинты, mediator, CORS и обработчик ошибок), поднятое в памяти тестового процесса:
// запросы идут через HttpClient напрямую в конвейер, без сети и порта. От боевого запуска отличается двумя вещами:
// 1) база — контейнер Postgres из PostgresFixture, а не movietracker-db на localhost:5432;
// 2) сидеров нет. Проверено на деле: фабрика не останавливает Bootstrap.cs до app.InitAndRunAsync(), поэтому сидеры при поднятии хоста
//    отрабатывают сами и в фоне вставляют каталог в тестовую базу — после очистки таблиц, то есть прямо в данные теста. Тесту нужна
//    пустая база, которую он наполняет сам, поэтому регистрации сидеров убираются
public class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Подмена строки подключения через ConfigureAppConfiguration: этот источник добавляется после appsettings.Development.json
        // и по построению перекрывает его. Проверено: UseSetting("ConnectionStrings:MovieTrackerDb", ...) в этой версии тоже побеждает файл,
        // но приоритет UseSetting зависит от того, как хост собирает конфигурацию; настоящую страховку даёт проверка в CreateHost ниже
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MovieTrackerDb"] = connectionString }));

        // ConfigureTestServices выполняется после всех регистраций Bootstrap.cs, поэтому удаление видит и сидеры. InitAndRunAsync берёт
        // все IAsyncInitializer из контейнера; когда их нет, он просто ничего не запускает и идёт дальше
        builder.ConfigureTestServices(services => services.RemoveAll<IAsyncInitializer>());
    }

    // Защита от тихой ошибки: если подмена не сработала, HTTP-тесты писали бы в рабочую базу. Лучше упасть до первого запроса
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var actual = scope.ServiceProvider.GetRequiredService<MovieTrackerDbContext>().Database.GetConnectionString();

        if (actual != connectionString)
        {
            host.Dispose();
            throw new InvalidOperationException("The test application did not pick up the test database connection string");
        }

        return host;
    }
}
