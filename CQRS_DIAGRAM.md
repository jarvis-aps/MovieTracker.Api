# Схема CQRS в MovieTracker

Обновлено: 2026-11-12 (состояние после задачи 3.24: watchlist ушёл в mediator). Раскладка сверху вниз: горизонтальная версия выросла до 3669 px в ширину (первая была 3060) и перестала читаться; стрелки от конвейера к handler'ам теперь выходят из рамки Mediator целиком. Схема составлена вручную по коду проекта и отражает путь запроса через mediator, который автоматический разбор кода не показывает (handler находится во время работы по типу сообщения). При изменении архитектуры схему нужно обновлять руками. Интерактивная версия с таблицей соответствия учебной картинке была опубликована как артефакт в сессии Claude Code; это снимок на состояние после задачи 3.21 (без проверки в конвейере), сам он не обновляется.

```mermaid
%%{init: {"flowchart": {"nodeSpacing": 16, "rankSpacing": 36, "wrappingWidth": 130, "padding": 8}}}%%
flowchart TB
    client["Клиент<br/>React-фронт: GET /movies, PATCH status<br/>остальное: прямые запросы"]

    subgraph http["Эндпоинты"]
        direction TB
        e1["PATCH /movies/{id}/status"]
        e2["PATCH /movies/{id}/notes"]
        e3["PATCH /movies/{id}/rating"]
        e4["POST /movies"]
        e5["GET /movies"]
        e6["PUT, DELETE /watchlist/{movieId}"]
    end

    subgraph med["Mediator (Mediator/)"]
        direction LR
        send["IMediator.Send"]
        timing["TimingBehavior<br/>замер времени, строка в лог"]
        validation["ValidationBehavior<br/>правила до handler'а,<br/>ошибка: handler не вызывается"]
        send --> timing
        timing --> validation
    end

    subgraph write["Запись: Commands/"]
        direction TB
        h1["SetStatusHandler"]
        h2["SetNotesHandler<br/>заметка до 500 символов"]
        h3["SetRatingHandler<br/>оценка 0–10, не NaN"]
        h4["CreateMovieHandler<br/>дубликат: null, то есть 400"]
        h6["AddToWatchlistHandler<br/>идемпотентный: гонка (нарушение<br/>индекса) считается успехом"]
        h7["RemoveFromWatchlistHandler<br/>один DELETE, идемпотентный"]
        v4["CreateMovieValidator<br/>название до 200 символов, год 1888 и до<br/>текущего плюс 5, жанры: список до 10"]
        writer["UserMovieDataWriter<br/>существование, найти или создать запись,<br/>сохранить с повтором при гонке"]
    end

    subgraph read["Чтение: Queries/"]
        direction TB
        h5["GetMoviesHandler<br/>фильтры, сортировка, страница"]
        reader["IWatchlistReader<br/>граница модуля"]
    end

    wl["WatchlistReader<br/>(Services/)"]
    db[("PostgreSQL, одна база<br/>Movies (TitleKey), Genres, GenreMovie,<br/>Users, UserMovieData, WatchlistItems")]

    client --> http
    e1 -->|SetStatusCommand| med
    e2 -->|SetNotesCommand| med
    e3 -->|SetRatingCommand| med
    e4 -->|CreateMovieCommand| med
    e5 -->|GetMoviesQuery| med
    e6 -->|"AddToWatchlistCommand,<br/>RemoveFromWatchlistCommand"| med

    med --> h1
    med --> h2
    med --> h3
    med --> h4
    med --> h6
    med --> h7
    med --> h5
    med -.->|проверяет| v4

    h1 --> writer
    h2 --> writer
    h3 --> writer
    writer -->|запись| db
    h4 -->|вставка фильма| db
    h6 -->|вставка в список| db
    h7 -->|удаление из списка| db
    db -->|чтение каталога| h5
    h5 --> reader
    reader -->|реализует| wl

    wl -->|чтение id| db

    classDef write fill:#fdf1e3,stroke:#b45309,color:#18202e
    classDef read fill:#e2f4f1,stroke:#0f766e,color:#18202e
    classDef infra fill:#efe9fc,stroke:#6140b8,color:#18202e
    class e1,e2,e3,e4,e6,h1,h2,h3,h4,h6,h7,v4,writer write
    class e5,h5,reader,wl read
    class send,timing,validation infra
    style http fill:none,stroke:#9aa5b8
    style med fill:none,stroke:#ab97e3
    style write fill:none,stroke:#e5a560
    style read fill:none,stroke:#62bbb0
```

Эндпоинты описаны в `Initializer/Bootstrap.cs`. Ответ идёт тем же путём назад. Результат в код ответа переводит эндпоинт: `null` даёт 404 у `PATCH` и 400 у `POST`, исключения о неверных границах (`InvalidRatingException`, `InvalidNotesException`, `InvalidPageSizeException`) дают 400, успех даёт 200 (у `POST` 201). У `PUT` и `DELETE` watchlist ответ команды `bool`: `false` (нет фильма или пользователя) даёт 404, `true` даёт 204 без тела. Нарушение правил `POST` (`RequestValidationException` из `ValidationBehavior`) эндпоинт переводит в 400 в формате `ValidationProblemDetails`: словарь `errors` по полям `title`, `year`, `genresId`, все ошибки сразу; handler при этом не вызывается. Замер (`TimingBehavior`) стоит снаружи проверки, поэтому пишет время и для отклонённых запросов.

## Чем проект отличается от учебной картинки «Client, Command Model, Write, Event, Read, Query Model»

| На учебной картинке | В проекте |
|---|---|
| Command Model | Команды и handler'ы в `Commands/`: `SetStatus`, `SetNotes`, `SetRating`, `CreateMovie`, `AddToWatchlist`, `RemoveFromWatchlist`; три `PATCH` делят механику записи `UserMovieDataWriter`, handler'ы watchlist самостоятельны (проверку существования повторяют намеренно) |
| Write, база | Запись через `DbContext` в PostgreSQL; уникальность фильма гарантирует индекс по ключу названия и году |
| Event | Нет. Handler'ы ничего не публикуют, события в плане этапа 3 |
| Materialized View, отдельная база чтения | Нет. Запрос читает те же таблицы, которые пишут команды |
| Query Model | `GetMoviesQuery` и `GetMoviesHandler` в `Queries/`; список watchlist handler берёт через `IWatchlistReader` (граница Movies и Watchlist, реализация `WatchlistReader`); чтение watchlist остаётся вне mediator |
| (нет на картинке) | Mediator с конвейером (`TimingBehavior` оборачивает каждый вызов, `ValidationBehavior` проверяет запрос до handler'а); через него теперь идут все операции записи |

Разделение команд и запросов в проекте сделано на уровне кода (сообщение, mediator, handler), а не на уровне хранилищ: база одна, событий нет.
