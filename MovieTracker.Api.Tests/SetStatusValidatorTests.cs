using MovieTracker.Api.Commands;
using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Tests;

// Правило PATCH /movies/{id}/status (решение 3.25) без базы и без mediator: статус должен быть значением enum Status
public class SetStatusValidatorTests
{
    private static List<ValidationError> Errors(Status status) =>
        new SetStatusValidator().Validate(new SetStatusCommand(1, 1, status)).ToList();

    // Все значения enum допустимы; список берётся из самого enum, поэтому новый статус не придётся добавлять сюда руками
    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Validate_EveryDefinedStatus_HasNoErrors(Status status)
    {
        Assert.Empty(Errors(status));
    }

    public static IEnumerable<object[]> AllStatuses() => Enum.GetValues<Status>().Select(status => new object[] { status });

    // Значения, которых в enum нет: 7 и -1 из запроса, 3 — то, во что EF-парсер превращал «Watched,Watching», и крайние значения int
    [Theory]
    [InlineData(7)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Validate_UndefinedStatus_ReportsOneErrorInStatusField(int value)
    {
        var errors = Errors((Status)value);

        Assert.Equal([new ValidationError("status", "Status must be one of: NotWatched, Watching, Watched")], errors);
    }
}
