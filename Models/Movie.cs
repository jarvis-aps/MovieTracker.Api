namespace MovieTracker.Api.Models;

public class Movie
{
    public int Id { get; set; }
    public int Year { get; set; }
    public float Rating { get; set; }
    public List<Genre> Genres { get; set; }
    public string Title { get; set; }
    public Status Status { get; set; }

    public Movie(string title,  Status status, List<Genre> genres, int year)
    {
        Title = title;
        Status =  status;
        Genres = genres;
        Year =  year;
    }
}
