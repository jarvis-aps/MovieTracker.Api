namespace MovieTracker.Api.Models;

public class Movie
{
    public int Id { get; set; }
    public int Year { get; set; }
    public float Rating { get; set; }
    public List<Genre> Genres { get; set; } = [];
    public string Title { get; set; }
    public Status Status { get; set; }
    public string? Notes { get; set; }

    public Movie(string title,  Status status, int year, string? notes)
    {
        Title = title;
        Status =  status;
        Year =  year;
        Notes = notes;
    }
}
