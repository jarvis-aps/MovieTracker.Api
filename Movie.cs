namespace MovieTracker.Api;

public class Movie
{
    public int Id { get; set; }
    public string Title { get; set; }
    public Status Status { get; set; }
    
    public Movie(string title)
    {
        Title = title;
    }
}