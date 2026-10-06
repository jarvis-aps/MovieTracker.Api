namespace MovieTracker.Api.Models;

public class User(string userName)
{
    public int Id { get; set; }
    public string UserName { get; set; } = userName;
    public List<UserMovieData> Movies { get; set; } = [];

}