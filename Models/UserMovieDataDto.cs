namespace MovieTracker.Api.Models;

// Ответ PATCH: только личная запись пользователя о фильме, без данных каталога
public record UserMovieDataDto(int MovieId, Status Status, float? Rating, string? Notes);
