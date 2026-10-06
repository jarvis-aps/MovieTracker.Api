namespace MovieTracker.Api.Models;

public class UserMovieData(int userId, int movieId, Status status)
{
    public int Id { get; set; }
    public int UserId { get; set; } = userId;
    public int MovieId { get; set; } = movieId;
    public float? Rating { get; set; }
    public Status Status { get; set; } = status;
    public string? Notes { get; set; }

}