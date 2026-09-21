namespace MovieTracker.Api.Models;

public class MovieQueryParameters(Status? status, int? genreId, SortBy? sortBy, bool? sortByDesc, int? page, int? pageSize, string? searchPart)
{
    public Status? Status { get; set; } = status;
    public int? GenreId { get; set; } = genreId;
    public SortBy? SortBy { get; set; } = sortBy;
    public bool? SortByDesc { get; set; } = sortByDesc;
    public int? Page { get; set; } = page;
    public int? PageSize { get; set; } = pageSize;
    public string? SearchPart { get; set; } = searchPart;

}