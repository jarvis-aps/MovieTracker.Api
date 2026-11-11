using MovieTracker.Api.Commands;
using MovieTracker.Api.Mediator;

namespace MovieTracker.Api.Tests;

// Правила POST /movies (решение 3.22) без базы и без mediator, время фиксированное: верхняя граница года зависит от текущего года
public class CreateMovieValidatorTests
{
    private class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // «Сейчас» 2026 год, значит верхняя граница года 2031
    private static CreateMovieValidator Validator(int currentYear = 2026) =>
        new(new FixedTimeProvider(new DateTimeOffset(currentYear, 6, 15, 12, 0, 0, TimeSpan.Zero)));

    private static List<ValidationError> Errors(CreateMovieCommand command, int currentYear = 2026) =>
        Validator(currentYear).Validate(command).ToList();

    [Fact]
    public void Validate_CorrectCommand_HasNoErrors()
    {
        Assert.Empty(Errors(new CreateMovieCommand("Матрица", 1999, [17, 19])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Validate_TitleMissingOrBlank_IsRequired(string? title)
    {
        var errors = Errors(new CreateMovieCommand(title!, 2000, []));

        Assert.Equal([new ValidationError("title", "Title is required")], errors);
    }

    [Fact]
    public void Validate_TitleOfTwoHundredCharacters_IsAccepted()
    {
        Assert.Empty(Errors(new CreateMovieCommand(new string('a', 200), 2000, [])));
    }

    [Fact]
    public void Validate_TitleOfTwoHundredAndOneCharacters_IsTooLong()
    {
        var errors = Errors(new CreateMovieCommand(new string('a', 201), 2000, []));

        Assert.Equal([new ValidationError("title", "Title must be at most 200 characters")], errors);
    }

    [Theory]
    [InlineData(1888)]
    [InlineData(2000)]
    [InlineData(2031)]
    public void Validate_YearInsideRange_IsAccepted(int year)
    {
        Assert.Empty(Errors(new CreateMovieCommand("Фильм", year, [])));
    }

    // 0 это пропущенный в теле год: он приходит как 0 и отклоняется тем же правилом
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1887)]
    [InlineData(2032)]
    [InlineData(99999)]
    public void Validate_YearOutsideRange_IsRejected(int year)
    {
        var errors = Errors(new CreateMovieCommand("Фильм", year, []));

        Assert.Equal([new ValidationError("year", "Year must be between 1888 and 2031")], errors);
    }

    // Верхняя граница берётся из часов, а не из константы: в 2030 году допустим год 2035
    [Fact]
    public void Validate_UpperYearFollowsTheClock()
    {
        Assert.Empty(Errors(new CreateMovieCommand("Фильм", 2035, []), currentYear: 2030));
        Assert.Equal([new ValidationError("year", "Year must be between 1888 and 2035")],
            Errors(new CreateMovieCommand("Фильм", 2036, []), currentYear: 2030));
    }

    [Fact]
    public void Validate_GenresMissing_IsRequired()
    {
        var errors = Errors(new CreateMovieCommand("Фильм", 2000, null!));

        Assert.Equal([new ValidationError("genresId", "GenresId is required")], errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Validate_GenresCountInsideLimit_IsAccepted(int count)
    {
        Assert.Empty(Errors(new CreateMovieCommand("Фильм", 2000, Enumerable.Range(1, count).ToList())));
    }

    [Fact]
    public void Validate_ElevenGenres_IsTooMany()
    {
        var errors = Errors(new CreateMovieCommand("Фильм", 2000, Enumerable.Range(1, 11).ToList()));

        Assert.Equal([new ValidationError("genresId", "GenresId must contain at most 10 items")], errors);
    }

    // Неизвестные id жанров не ошибка: handler по-прежнему молча их пропускает
    [Fact]
    public void Validate_UnknownGenreIds_AreNotAnError()
    {
        Assert.Empty(Errors(new CreateMovieCommand("Фильм", 2000, [9999, -1, 0])));
    }

    // Все нарушения приходят сразу, по одной ошибке на поле
    [Fact]
    public void Validate_EverythingWrong_ReportsEveryFieldAtOnce()
    {
        var errors = Errors(new CreateMovieCommand(null!, 0, null!));

        Assert.Equal(["title", "year", "genresId"], errors.Select(e => e.Field));
    }
}
