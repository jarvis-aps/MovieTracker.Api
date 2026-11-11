using Microsoft.Extensions.DependencyInjection;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;
using MovieTracker.Api.Queries;

namespace MovieTracker.Api.Tests;

// Запрос целиком: настоящий mediator, handler найден сканированием сборки (AddMediator) и настоящий Postgres (см. MediatorTestProvider).
// Сам расчёт страницы (фильтры, сортировки, границы) проверяют GetMoviesTests; здесь — что запрос проходит через mediator
[Collection(PostgresCollection.Name)]
public class GetMoviesQueryTests(PostgresFixture postgres)
{
    private static ServiceProvider BuildProvider(TestDatabase db) => MediatorTestProvider.Build(db);

    [Fact]
    public async Task Send_ReturnsPersonalPageAndTotalCount()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var alpha = await db.AddMovieAsync("Alpha");
        await db.AddMovieAsync("Bravo");
        await db.AddMovieAsync("Charlie");
        await db.AddUserDataAsync(userId, alpha, Status.Watched, rating: 8, notes: "заметка");
        await using var provider = BuildProvider(db);
        using var scope = provider.CreateScope();

        var page = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetMoviesQuery(userId, PageSize: 2));

        Assert.Equal(["Alpha", "Bravo"], page.Items.Select(m => m.Title));
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(Status.Watched, page.Items[0].status);
        Assert.Equal(8, page.Items[0].rating);
        Assert.Equal("заметка", page.Items[0].Notes);
    }

    // Ошибка handler'а проходит сквозь mediator и замер как есть: превратить её в 400 по-прежнему должен эндпоинт
    [Fact]
    public async Task Send_PageNotPositive_ExceptionPassesThroughMediator()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        await using var provider = BuildProvider(db);
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidPageSizeException>(() => mediator.Send(new GetMoviesQuery(userId, Page: 0)));
        await Assert.ThrowsAsync<InvalidPageSizeException>(() => mediator.Send(new GetMoviesQuery(userId, PageSize: -5)));
    }

    // Решение 3.15: дефолты страницы лежат в самом запросе, а не в сервисе. Без них [AsParameters] не подставил бы 1 и 100
    [Fact]
    public void Query_WithoutPaging_DefaultsToFirstPageOfHundred()
    {
        var query = new GetMoviesQuery(UserId: 1);

        Assert.Equal(1, query.Page);
        Assert.Equal(100, query.PageSize);
    }

    // Замер подключён к запросу без единой строки в самом handler'е
    [Fact]
    public async Task AddMediator_ConnectsTimingBehaviorToQueries()
    {
        var db = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(db);
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<GetMoviesQuery, MoviesPage>>();

        Assert.IsType<TimingBehavior<GetMoviesQuery, MoviesPage>>(Assert.Single(behaviors));
    }
}
