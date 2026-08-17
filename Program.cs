using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Data;
using MovieTracker.Api.Models;
using MovieTracker.Api.Services;

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

builder.Services.AddScoped<MovieService>();

var app = builder.Build();

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

app.MapGet("/movies", async (MovieService service) => await service.GetMoviesAsync());

app.MapPatch("/movies/{id}/status", async (int id, Status status, MovieService service) =>
{
    var result = await service.UpdateStatusAsync(id, status);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPost("/movies", async (MovieService service, NewMovie newMovie) =>
{
    var result = await service.CreateMovieAsync(newMovie);
    return result is null ? Results.BadRequest() : Results.Created($"/movies/{result.Id}", result);
});

app.Run();
