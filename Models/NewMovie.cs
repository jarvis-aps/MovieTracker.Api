namespace MovieTracker.Api.Models;

public class NewMovie
{
    public string Title { get; }
    public Status Status { get; }
    public int Year { get; set; }
    public List<int> GenresId { get; set; }
    public string? Notes { get; }

    public NewMovie(string title,  Status status, List<int> genresId,  int year, string? notes)
    {
        Title = title;
        Status = status;
        GenresId = genresId;
        Year = year;
        Notes = notes;
    }
}