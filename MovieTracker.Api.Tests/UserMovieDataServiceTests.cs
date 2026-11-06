using Microsoft.Extensions.Logging.Abstractions;
using MovieTracker.Api.Exceptions;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

namespace MovieTracker.Api.Tests;

[Collection(PostgresCollection.Name)]
public class UserMovieDataServiceTests(PostgresFixture postgres)
{
    private static UserMovieDataService CreateService(TestDatabase db) =>
        new(db.NewContext(), NullLogger<UserMovieDataService>.Instance);

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

        Assert.Equal(new UserMovieDataDto(movieId, Status.NotWatched, rating), result);
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

        Assert.Equal(new UserMovieDataDto(movieId, Status.NotWatched, null), result);
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

        Assert.Equal(new UserMovieDataDto(movieId, Status.Watched, 8), result);
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

        Assert.Equal(new UserMovieDataDto(movieId, Status.Watching, null), bobsResult);
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
