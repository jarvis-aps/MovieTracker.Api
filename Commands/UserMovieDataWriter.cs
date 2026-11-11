using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Initializer;
using MovieTracker.Api.Models;
using Npgsql;

namespace MovieTracker.Api.Commands;

// Механика записи личных данных, общая для команд статуса, заметки и оценки (решение 3.18: помощник через DI, а не базовый класс
// и не копии в handler'ах). Про Movies и Users знает в одном месте: проверка, что фильм и пользователь существуют. Логики самих операций
// здесь нет: проверки границ и выбор изменяемого поля остаются в handler'ах. Ответ — только личная запись, поэтому данные каталога
// и watchlist помощнику не нужны
public class UserMovieDataWriter(MovieTrackerDbContext context, ILogger<UserMovieDataWriter> logger)
{
    // Handler'ы возвращают null, если фильм или пользователь не найден (эндпоинт отвечает 404)
    public async Task<bool> ExistAsync(int movieId, int userId)
    {
        return await context.Movies.AnyAsync(m => m.Id == movieId) && await context.Users.AnyAsync(u => u.Id == userId);
    }

    // Находит или создаёт запись, применяет изменение и сохраняет. Два одновременных первых PATCH одной пары оба не находят
    // запись и оба вставляют: проигравшего останавливает уникальный индекс (UserId, MovieId)
    public async Task<UserMovieDataDto> SaveAsync(int movieId, int userId, Action<UserMovieData> apply)
    {
        var data = await GetOrCreateAsync(movieId, userId);
        apply(data);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // В отличие от watchlist это нельзя считать успехом: у проигравшего своё изменение (статус, оценка или заметка), и оно ещё не записано.
            // Отсоединяем свою несохранённую вставку, находим запись победителя и применяем то же изменение к ней.
            // Повтор один: индекс срабатывает, только когда победитель уже закоммитил запись, поэтому теперь мы её видим
            logger.LogInformation("User data of user {0} for movie {1} was created concurrently, applying the change to it", userId, movieId);
            context.Entry(data).State = EntityState.Detached;

            data = await GetOrCreateAsync(movieId, userId);
            apply(data);
            await context.SaveChangesAsync();
        }

        return ToDto(data);
    }

    private async Task<UserMovieData> GetOrCreateAsync(int movieId, int userId)
    {
        var data = await context.UserMovieData.FirstOrDefaultAsync(d => d.MovieId == movieId && d.UserId == userId);

        if (data != null)
            return data;

        data = new UserMovieData(userId, movieId, Status.NotWatched);
        context.UserMovieData.Add(data);

        return data;
    }

    private static UserMovieDataDto ToDto(UserMovieData data) => new(data.MovieId, data.Status, data.Rating, data.Notes);
}
