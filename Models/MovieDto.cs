namespace MovieTracker.Api.Models;

public record MovieDto(int Id, string Title, List<GenreDto> Genres,  int Year, float? rating, Status? status);