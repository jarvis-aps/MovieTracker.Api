namespace MovieTracker.Api.Models;

public class Movie
{
    public int Id { get; set; }
    public int Year { get; set; }
    public List<Genre> Genres { get; set; } = [];
    public string Title { get; set; }

    // Ключ сравнения названий: lower(btrim(Title)), его считает и хранит сама база (вычисляемая колонка), код его не задаёт.
    // На нём и годе стоит уникальный индекс: «Матрица», «матрица» и «Матрица » с одним годом — один фильм (решение 3.21)
    public string TitleKey { get; private set; } = null!;

    public List<UserMovieData> MoviesData { get; set; } = [];
    public List<WatchlistItem> WatchlistItems { get; set; } = [];
    
    public Movie(string title, int year)
    {
        Title = title;
        Year =  year;
    }
}
