namespace MovieTracker.Api.Models;

public class WatchlistItem(int userId, int movieId)
{
    public int Id { get; set; }
    public int UserId { get; set; } = userId;
    public int MovieId { get; set; } = movieId;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
