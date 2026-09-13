namespace MovieTracker.Api.Models;

public class NewMovie
{
    public string Title { get; }
    public Status Status { get; }
    public int Year { get; set; }
    public List<Genre> Genres { get; set; }
    public string? Notes { get; }

    public NewMovie(string title,  Status status, List<Genre> genres,  int year, string? notes)
    {
        Title = title;
        Status = status;
        Genres = genres;
        Year = year;
        Notes = notes;
    }
}