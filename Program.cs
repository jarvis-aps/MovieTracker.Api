using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Data;
using MovieTracker.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<MovieTrackerDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("MovieTrackerDb")));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MovieTrackerDbContext>();

    if (!db.Movies.Any())
    {
        var movie1 = new Movie("Тед Лассо", Status.NotWatched) { Status = Status.NotWatched };
        var movie2 = new Movie("Ананасовый экспресс", Status.NotWatched) { Status = Status.NotWatched };
        var movie3 = new Movie("Пляжный бездельник", Status.NotWatched) { Status = Status.NotWatched };
        db.Movies.Add(movie1);
        db.Movies.Add(movie2);
        db.Movies.Add(movie3);      
        db.SaveChanges();
    }
}

app.MapGet("/movies", async (MovieTrackerDbContext db) =>
{
    List<Movie> movieList = await db.Movies.ToListAsync();
    return movieList;
});

app.MapPatch("/movies/{id}/status", async (int id, MovieTrackerDbContext db, Status status) =>
{
    var currentMovie = await db.Movies.FirstOrDefaultAsync(m => m.Id == id);

    if (currentMovie == null)
        return Results.NotFound();
    
    currentMovie.Status = status;
    await db.SaveChangesAsync();
    
    return Results.Ok(currentMovie.Status);
});

app.MapPost("/movies", async (MovieTrackerDbContext db, NewMovie newMovie) =>
{
    var movie = new Movie(newMovie.Title, newMovie.Status);
    
    if (db.Movies.Any(m => m.Title == newMovie.Title))
    {
        return Results.BadRequest();
    }
    
    await db.Movies.AddAsync(movie);
    await db.SaveChangesAsync();
    
    return Results.Created($"/movies/{movie.Id}", movie);
});

app.Run();
