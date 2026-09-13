namespace MovieTracker.Api.Models;

public class MovieQueryParameters(Status? status, Genre? genre, SortBy? sortBy, bool? sortByDesc, int? page, int? pageSize, string? searchPart)
{
    public Status? Status { get; set; } = status;
    public Genre? Genre { get; set; } = genre;
    public SortBy? SortBy { get; set; } = sortBy;
    public bool? SortByDesc { get; set; } = sortByDesc;
    public int? Page { get; set; } = page;
    public int? PageSize { get; set; } = pageSize;
    public string? SearchPart { get; set; } = searchPart;

}