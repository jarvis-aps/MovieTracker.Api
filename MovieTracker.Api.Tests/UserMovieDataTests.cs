using Microsoft.Extensions.DependencyInjection;
using MovieTracker.Api.Commands;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Tests;

// Общее для трёх команд записи личных данных: механика UserMovieDataWriter (найти или создать запись, сохранить с повтором при гонке,
// не трогать чужое). Проверяется через mediator на настоящем DI и Postgres, как работает приложение
[Collection(PostgresCollection.Name)]
public class UserMovieDataTests(PostgresFixture postgres)
{
    private const int ParallelCalls = 30;
    private const int Rounds = 10;

    // Как запрос в приложении: свой scope, а значит и свой DbContext, на каждый вызов
    private static async Task<T> SendAsync<T>(ServiceProvider provider, IRequest<T> request)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    // Гонка первого PATCH, воспроизведённая точно, а не случайно. «Победитель» вставил запись пары (статус) и ещё не завершил транзакцию:
    // «проигравший» (оценка) её не видит, пытается вставить свою и встаёт на уникальном индексе. Когда победитель завершается,
    // проигравший получает нарушение уникальности и обязан применить свою оценку к записи победителя. Если повтора нет, будет ошибка,
    // если повтор не применяет изменение, оценка потеряется: тест падает в обоих случаях, каждый раз, а не по вероятности
    [Fact]
    public async Task Send_LoserOfFirstWriteRace_AppliesItsChangeToTheWinnersRecord()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await using var provider = MediatorTestProvider.Build(db);

        await using var winner = await db.InsertUserDataWithoutCommitAsync(userId, movieId, Status.Watched);
        var loser = SendAsync(provider, new SetRatingCommand(movieId, userId, 7));
        await db.WaitUntilWriterIsBlockedAsync(loser);

        await winner.CommitAsync();
        var result = await loser;

        Assert.Equal(new UserMovieDataDto(movieId, Status.Watched, 7, null), result);
        Assert.Equal(1, await db.CountUserDataAsync(userId, movieId));
        var saved = await db.GetUserDataAsync(userId, movieId);
        Assert.Equal(Status.Watched, saved?.Status);
        Assert.Equal(7, saved?.Rating);
    }

    // Та же гонка в большом масштабе, но случайная: половина вызовов ставит статус, половина оценку, все стартуют разом. Ловит ошибки
    // и полную потерю изменений одного вида, а потерю единичного проигравшего не ловит (его изменение чаще повторяют другие вызовы того же
    // вида): для этого есть точный тест выше. Одного раунда мало, гонка срабатывает не каждый раз
    [Fact]
    public async Task Send_ParallelFirstCommands_AllSucceedAndNoChangeIsLost()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        await using var provider = MediatorTestProvider.Build(db);

        for (var round = 0; round < Rounds; round++)
        {
            var movieId = await db.AddMovieAsync($"Фильм {round}");

            await Task.WhenAll(Enumerable.Range(0, ParallelCalls).Select(i => Task.Run(() => i % 2 == 0
                ? SendAsync(provider, new SetStatusCommand(movieId, userId, Status.Watched))
                : SendAsync(provider, new SetRatingCommand(movieId, userId, 7)))));

            Assert.Equal(1, await db.CountUserDataAsync(userId, movieId));
            var saved = await db.GetUserDataAsync(userId, movieId);
            Assert.Equal(Status.Watched, saved?.Status);
            Assert.Equal(7, saved?.Rating);
        }
    }

    [Fact]
    public async Task Send_ForExistingRecord_KeepsOtherFields()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddUserDataAsync(userId, movieId, Status.Watched, rating: 8);
        await using var provider = MediatorTestProvider.Build(db);

        var result = await SendAsync(provider, new SetNotesCommand(movieId, userId, "заметка"));

        Assert.Equal(new UserMovieDataDto(movieId, Status.Watched, 8, "заметка"), result);
    }

    // Ответ команд статуса и оценки показывает заметку, записанную раньше: DTO общий для трёх команд
    [Fact]
    public async Task Send_StatusAndRating_ResponseContainsExistingNotes()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddUserDataAsync(userId, movieId, Status.NotWatched, notes: "посмотреть с друзьями");
        await using var provider = MediatorTestProvider.Build(db);

        var afterStatus = await SendAsync(provider, new SetStatusCommand(movieId, userId, Status.Watched));
        var afterRating = await SendAsync(provider, new SetRatingCommand(movieId, userId, 8));

        Assert.Equal("посмотреть с друзьями", afterStatus?.Notes);
        Assert.Equal("посмотреть с друзьями", afterRating?.Notes);
    }

    [Fact]
    public async Task Send_DoesNotTouchAnotherUsersRecord()
    {
        var db = await postgres.CreateDatabaseAsync();
        var alice = await db.AddUserAsync("alice");
        var bob = await db.AddUserAsync("bob");
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddUserDataAsync(alice, movieId, Status.Watched, rating: 9);
        await using var provider = MediatorTestProvider.Build(db);

        var bobsResult = await SendAsync(provider, new SetStatusCommand(movieId, bob, Status.Watching));

        Assert.Equal(new UserMovieDataDto(movieId, Status.Watching, null, null), bobsResult);
        var alicesRecord = await db.GetUserDataAsync(alice, movieId);
        Assert.Equal(Status.Watched, alicesRecord?.Status);
        Assert.Equal(9, alicesRecord?.Rating);
    }
}
