namespace MovieTracker.Api.Models;

public record MovieDto(int Id, string Title, Status Status, List<Genre> Genres,  int Year, float  Rating, string? Notes);