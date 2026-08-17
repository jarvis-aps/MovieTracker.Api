namespace MovieTracker.Api.Models;

public class NewMovie
{
    public string Title { get; }
    public Status Status { get; }

    public NewMovie(string title,  Status status)
    {
        Title = title;
        Status = status;
    }
}