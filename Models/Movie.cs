namespace MovieTracker.Api.Models;

public class Movie
{
    public int Id { get; set; }
    public string Title { get; set; }
    public Status Status { get; set; }

    public Movie(string title,  Status status)
    {
        Title = title;
        Status =  status;
    }
}
