using Microsoft.Extensions.Logging.Abstractions;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Tests;

[Collection(PostgresCollection.Name)]
public class UserMovieDataServiceTests(PostgresFixture postgres)
{
    private const int ParallelCalls = 30;
    private const int Rounds = 10;

    private static UserMovieDataService CreateService(TestDatabase db) =>
        new(db.NewContext(), NullLogger<UserMovieDataService>.Instance);

    // Гонка первого PATCH (открытый вопрос 3.2). Половина вызовов ставит статус, половина — оценку: если проигравшему
    // просто сказать «уже сделано» (как в watchlist), его изменение потеряется, и в записи не окажется статуса или оценки.
    // Одного раунда мало — гонка срабатывает не каждый раз
    [Fact]
    public async Task Update_ParallelFirstCalls_AllSucceedAndNoChangeIsLost()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();

        for (var round = 0; round < Rounds; round++)
        {
            var movieId = await db.AddMovieAsync($"Фильм {round}");

            await Task.WhenAll(Enumerable.Range(0, ParallelCalls).Select(i => Task.Run(() => i % 2 == 0
                ? CreateService(db).UpdateStatusAsync(movieId, userId, Status.Watched)
                : CreateService(db).UpdateRatingAsync(movieId, userId, 7))));

            Assert.Equal(1, await db.CountUserDataAsync(userId, movieId));
            var saved = await db.GetUserDataAsync(userId, movieId);
            Assert.Equal(Status.Watched, saved?.Status);
            Assert.Equal(7, saved?.Rating);
        }
    }

    [Theory]
    [InlineData(-0.5f)]
    [InlineData(10.5f)]
    public async Task UpdateRatingAsync_OutOfRange_ThrowsAndSavesNothing(float rating)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        await Assert.ThrowsAsync<InvalidRatingException>(() => CreateService(db).UpdateRatingAsync(movieId, userId, rating));
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(10f)]
    public async Task UpdateRatingAsync_BoundaryValues_AreAccepted(float rating)
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        var result = await CreateService(db).UpdateRatingAsync(movieId, userId, rating);

        Assert.Equal(new UserMovieDataDto(movieId, Status.NotWatched, rating, null), result);
    }

    [Fact]
    public async Task UpdateStatusAsync_NoUserDataYet_CreatesSingleRowAndRepeatDoesNotDuplicate()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        var first = await CreateService(db).UpdateStatusAsync(movieId, userId, Status.Watching);
        var second = await CreateService(db).UpdateStatusAsync(movieId, userId, Status.Watched);

        Assert.Equal(Status.Watching, first?.Status);
        Assert.Equal(Status.Watched, second?.Status);
        Assert.Equal(1, await db.CountUserDataAsync(userId, movieId));
    }

    // Раньше PATCH notes нигде не проверялся
    [Fact]
    public async Task UpdateNotesAsync_CreatesRecordAndStoresNotes()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        var result = await CreateService(db).UpdateNotesAsync(movieId, userId, "посмотреть с друзьями");

        Assert.Equal(new UserMovieDataDto(movieId, Status.NotWatched, null, "посмотреть с друзьями"), result);
        var saved = await db.GetUserDataAsync(userId, movieId);
        Assert.Equal("посмотреть с друзьями", saved?.Notes);
    }

    [Fact]
    public async Task Update_ForExistingRecord_KeepsOtherFields()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddUserDataAsync(userId, movieId, Status.Watched, rating: 8);

        var result = await CreateService(db).UpdateNotesAsync(movieId, userId, "заметка");

        Assert.Equal(new UserMovieDataDto(movieId, Status.Watched, 8, "заметка"), result);
    }

    // Ответ PATCH status/rating показывает заметку, записанную раньше: DTO общий для трёх PATCH
    [Fact]
    public async Task UpdateStatusAndRating_ResponseContainsExistingNotes()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddUserDataAsync(userId, movieId, Status.NotWatched, notes: "посмотреть с друзьями");

        var afterStatus = await CreateService(db).UpdateStatusAsync(movieId, userId, Status.Watched);
        var afterRating = await CreateService(db).UpdateRatingAsync(movieId, userId, 8);

        Assert.Equal("посмотреть с друзьями", afterStatus?.Notes);
        Assert.Equal("посмотреть с друзьями", afterRating?.Notes);
    }

    [Fact]
    public async Task Update_DoesNotTouchAnotherUsersRecord()
    {
        var db = await postgres.CreateDatabaseAsync();
        var alice = await db.AddUserAsync("alice");
        var bob = await db.AddUserAsync("bob");
        var movieId = await db.AddMovieAsync("Дюна");
        await db.AddUserDataAsync(alice, movieId, Status.Watched, rating: 9);

        var bobsResult = await CreateService(db).UpdateStatusAsync(movieId, bob, Status.Watching);

        Assert.Equal(new UserMovieDataDto(movieId, Status.Watching, null, null), bobsResult);
        var alicesRecord = await db.GetUserDataAsync(alice, movieId);
        Assert.Equal(Status.Watched, alicesRecord?.Status);
        Assert.Equal(9, alicesRecord?.Rating);
    }

    [Fact]
    public async Task Update_UnknownMovieOrUser_ReturnsNullAndCreatesNothing()
    {
        var db = await postgres.CreateDatabaseAsync();
        var userId = await db.AddUserAsync();
        var movieId = await db.AddMovieAsync("Дюна");

        Assert.Null(await CreateService(db).UpdateStatusAsync(9999, userId, Status.Watched));
        Assert.Null(await CreateService(db).UpdateStatusAsync(movieId, 9999, Status.Watched));
        Assert.Null(await CreateService(db).UpdateNotesAsync(9999, userId, "x"));
        Assert.Null(await CreateService(db).UpdateNotesAsync(movieId, 9999, "x"));
        Assert.Null(await CreateService(db).UpdateRatingAsync(9999, userId, 5));
        Assert.Null(await CreateService(db).UpdateRatingAsync(movieId, 9999, 5));
        Assert.Equal(0, await db.CountUserDataAsync(userId, movieId));
    }
}
