namespace MovieTracker.Api.Models;

public class Movie
{
    public int Id { get; set; }
    public int Year { get; set; }
    public List<Genre> Genres { get; set; } = [];
    public string Title { get; set; }
    public List<UserMovieData> MoviesData { get; set; } = [];
    
    public Movie(string title, int year)
    {
        Title = title;
        Year =  year;
    }
}
