namespace MovieTracker.Api.Models;

public record MovieDto(int Id, string Title, Status Status, List<GenreDto> Genres,  int Year, float  Rating, string? Notes);