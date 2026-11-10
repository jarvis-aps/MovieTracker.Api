namespace MovieTracker.Api.Models;

// Страница каталога и общее число фильмов под теми же фильтрами (без учёта page/pageSize)
public record MoviesPage(List<MovieDto> Items, int TotalCount);
