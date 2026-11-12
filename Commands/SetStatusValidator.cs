using MovieTracker.Api.Mediator;
using MovieTracker.Api.Models;

namespace MovieTracker.Api.Commands;

// Правило для PATCH /movies/{id}/status (решение 3.25): статус должен быть значением enum Status. Привязка параметров .NET превращает
// любое число (7, -1) и склейку имён («Watched,Watching» читается как 3) в значение enum и пропускает дальше, поэтому «есть ли такое
// значение» проверяем здесь. Числа, которые в enum есть (0–2), остаются допустимыми (вариант Б). Имя поля как в запросе: status
public class SetStatusValidator : IValidator<SetStatusCommand>
{
    // Список имён берётся из enum, поэтому новый статус попадёт в сообщение сам
    private static readonly string AllowedNames = string.Join(", ", Enum.GetNames<Status>());

    public IEnumerable<ValidationError> Validate(SetStatusCommand command)
    {
        if (!Enum.IsDefined(command.Status))
            yield return new ValidationError("status", $"Status must be one of: {AllowedNames}");
    }
}
