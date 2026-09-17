using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MovieTracker.Api.Data;
using MovieTracker.Api.Exceptions;
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
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is BadHttpRequestException badRequestException)
            context.Response.StatusCode = badRequestException.StatusCode;
        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new ProblemDetailsContext { HttpContext = context });
    });
});

app.UseStatusCodePages();

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
        var movie1 = new Movie("Тед Лассо", Status.NotWatched, [Genre.Comedy, Genre.Sport], 2020, null) { Status = Status.NotWatched };
        var movie2 = new Movie("Ананасовый экспресс", Status.NotWatched, [Genre.Action, Genre.Comedy], 2008, null) { Status = Status.NotWatched };
        var movie3 = new Movie("Пляжный бездельник", Status.NotWatched, [Genre.Action, Genre.Comedy], 2019, "example") { Status = Status.NotWatched };
        db.Movies.Add(movie1);
        db.Movies.Add(movie2);
        db.Movies.Add(movie3);      
        db.SaveChanges();
    }
}

app.MapGet("/movies", async (MovieService service, [AsParameters]MovieQueryParameters movieQueryParameters) =>
{
    List<MovieDto> result;
    
    try
    {
        result = await service.GetMoviesAsync(movieQueryParameters);
    }
    catch (InvalidPageSizeException exception)
    {
        return Results.BadRequest(exception.ErrorMessage);
    }
    
    return Results.Ok(result);
});

app.MapPatch("/movies/{id}/status", async (int id, Status status, MovieService service) =>
{
    var result = await service.UpdateStatusAsync(id, status);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPatch("/movies/{id}/notes", async (int id, string notes, MovieService service) =>
{
    var result = await service.UpdateNotesAsync(id, notes);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPatch("/movies/{id}/rating", async (int id, float rating, MovieService service) =>
{
    MovieDto? result;
    
    try
    {
        result = await service.UpdateRatingAsync(id, rating);
    }
    catch (InvalidRatingException exception)
    {
        return Results.BadRequest(exception.ErrorMessage);
    }

    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPost("/movies", async (MovieService service, NewMovie newMovie) =>
{
    var result = await service.CreateMovieAsync(newMovie);
    return result is null ? Results.BadRequest() : Results.Created($"/movies/{result.Id}", result);
});

app.Run();
