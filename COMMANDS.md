# COMMANDS.md

Шпаргалка по терминальным командам — по сценарию, а не по алфавиту. Ищи по симптому/ситуации, не по названию команды.

Пополняется по мере того, как встречаются новые кейсы — не пытаться предугадать заранее.

## Обычный запуск проекта

```bash
dotnet build                    # сборка
dotnet run                      # запуск (http://localhost:5066, https://localhost:7131)
dotnet watch run                # запуск с hot reload
```

## EF Core — обычный цикл: добавил/поменял модель

```bash
dotnet ef migrations add <Name>     # сгенерировать миграцию по текущей модели
dotnet ef database update           # применить к реальной Postgres
```

`<Name>` — по смыслу изменения (`NewFields`, `Notes`, `GenresManyToMany`), не техническое `Update1`.

## Симптом: `dotnet ef database update` падает с `PendingModelChangesWarning`

Значит модель (C#-классы) успела измениться **после** того, как миграция была сгенерирована (`migrations add`) — файл миграции и текущая модель разошлись.

Два разных случая — важно определить, какой именно:

**А. Миграция ещё не была применена к реальной базе** (последняя `database update` не проходила, база не тронута) — самый дешёвый путь, удалить и пересоздать:
```bash
dotnet ef migrations remove         # снести неприменённую миграцию
dotnet ef migrations add <Name>     # сгенерировать заново, уже по актуальной модели
dotnet ef database update
```

**Б. Миграция уже применена к реальной базе** — `migrations remove` тут не поможет (нельзя удалить то, что уже часть истории применённых изменений). Нужна вторая, "догоняющая" миграция:
```bash
dotnet ef migrations add <NameFix>   # например updateNewFields, updateRatingType
dotnet ef database update
```
Прецедент — Задача 2.3 (`Rating` сначала `decimal`, потом `float`, миграция `NewFields` уже была применена, догнали `updateNewFields`).

Как проверить, какой случай, если не помнишь сам:
```bash
dotnet ef migrations has-pending-model-changes
```

## Проверить, что реально лежит в базе

```bash
docker exec -it movietracker-db psql -U movietracker -d movietracker
```
Внутри `psql`:
```sql
\d "Movies"      -- кавычки обязательны: Postgres по умолчанию сворачивает регистр,
                  -- EF Core создаёт таблицы с регистром как у C#-класса
\dt               -- список всех таблиц
```

## Проверить эндпоинт вручную (curl)

```bash
curl http://localhost:5066/movies

curl -X POST http://localhost:5066/movies \
  -H "Content-Type: application/json" \
  -d '{"title":"Начало","year":2010,"genresId":[1,2]}'

curl -X PATCH "http://localhost:5066/movies/1/status?status=Watched"
```
Тело (`-d`) — только для `POST`/там, где данные идут в JSON. Query-параметры (`?status=`) — для `PATCH`, как уже сложилось в проекте.

## Добавил новую запись в сидер, а она не появляется в базе

Все сидеры в проекте идемпотентны через `AnyAsync()` — "если в таблице есть **хоть одна** строка, весь блок сидинга пропускается целиком". Значит если таблица уже непустая (с прошлого запуска), новая запись, дописанная в сидер, никогда не выполнится — не хватит просто перезапустить `dotnet run`.

Нужно сначала очистить таблицу, чтобы `AnyAsync()` снова вернул `false`:
```bash
docker exec -it movietracker-db psql -U movietracker -d movietracker -c 'DELETE FROM "TableName";'
dotnet run
```
Прецедент — сидинг `UserMovieData` в задаче 3.2, второй тестовый ряд не появлялся, пока не очистили таблицу.

## Сравнить поведение до/после правки, не теряя изменений

```bash
git stash        # временно отложить незакоммиченные правки
# ...проверить старое поведение...
git stash pop    # вернуть правки обратно
```
Использовалось в 2.7, чтобы подтвердить: сырой `BadHttpRequestException`-дамп был и до рефакторинга, не регресс.
