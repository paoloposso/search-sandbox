using Microsoft.EntityFrameworkCore;
using OpenSearch.Client;
using SearchGateway.Data;
using SearchGateway.Models;
using SearchGateway.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// Postgres DB Context
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// OpenSearch Configuration
var openSearchUrl = builder.Configuration.GetValue<string>("OpenSearch:Url") ?? "http://localhost:9200";
var connectionSettings = new ConnectionSettings(new Uri(openSearchUrl))
    .DefaultIndex("movies")
    .DisableDirectStreaming();

builder.Services.AddSingleton<IOpenSearchClient>(new OpenSearchClient(connectionSettings));
builder.Services.AddSingleton<OpenSearchService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Automatically ensure the database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        // For development, we drop and recreate database to refresh schema cleanly
        dbContext.Database.EnsureDeleted();
        dbContext.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error verifying/creating database on startup: {ex.Message}");
    }
}

// ==========================================
// Postgres Endpoints
// ==========================================

// Endpoint to retrieve movies directly from Postgres (Eager Loading relations)
app.MapGet("/api/movies", async (AppDbContext dbContext) =>
{
    var movies = await dbContext.Movies
        .Include(m => m.Director)
        .Include(m => m.Genres)
        .Include(m => m.MovieActors)
            .ThenInclude(ma => ma.Actor)
        .ToListAsync();
        
    return Results.Ok(movies);
});

// Endpoint to seed normalized movies into Postgres from JSON file
app.MapPost("/api/db/seed", async (AppDbContext dbContext) =>
{
    try
    {
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
        string? jsonPath = null;
        while (!string.IsNullOrEmpty(currentDir))
        {
            var testPath = Path.Combine(currentDir, "movies_mock_data.json");
            if (File.Exists(testPath))
            {
                jsonPath = testPath;
                break;
            }
            currentDir = Path.GetDirectoryName(currentDir);
        }

        if (jsonPath == null)
        {
            return Results.Problem("Could not find movies_mock_data.json in any parent directory.");
        }

        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var rawMovies = JsonSerializer.Deserialize<List<RawMovie>>(jsonString, options);

        if (rawMovies == null || !rawMovies.Any())
        {
            return Results.BadRequest("No movies found in JSON dataset.");
        }

        // Clear existing tables in correct order of dependency
        dbContext.MovieActors.RemoveRange(dbContext.MovieActors);
        dbContext.Movies.RemoveRange(dbContext.Movies);
        dbContext.Directors.RemoveRange(dbContext.Directors);
        dbContext.Actors.RemoveRange(dbContext.Actors);
        dbContext.Genres.RemoveRange(dbContext.Genres);
        await dbContext.SaveChangesAsync();

        // Local tracking dictionaries to reuse entities and prevent duplicates
        var directorsDict = new Dictionary<string, Director>(StringComparer.OrdinalIgnoreCase);
        var actorsDict = new Dictionary<string, Actor>(StringComparer.OrdinalIgnoreCase);
        var genresDict = new Dictionary<string, Genre>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawMovies)
        {
            // Resolve/Create Director
            if (!directorsDict.TryGetValue(raw.Director, out var director))
            {
                director = new Director { Name = raw.Director };
                directorsDict[raw.Director] = director;
                await dbContext.Directors.AddAsync(director);
            }

            // Resolve/Create Genres
            var movieGenres = new List<Genre>();
            foreach (var genreName in raw.Genres)
            {
                if (!genresDict.TryGetValue(genreName, out var genre))
                {
                    genre = new Genre { Name = genreName };
                    genresDict[genreName] = genre;
                    await dbContext.Genres.AddAsync(genre);
                }
                movieGenres.Add(genre);
            }

            // Create Movie (EF Core auto-generates ID)
            var movie = new Movie
            {
                Title = raw.Title,
                Plot = raw.Plot,
                ReleaseYear = raw.ReleaseYear,
                Rating = raw.Rating,
                Director = director,
                Genres = movieGenres
            };
            await dbContext.Movies.AddAsync(movie);

            // Resolve/Create Actors & Join table MovieActor
            foreach (var rawActor in raw.Actors)
            {
                if (!actorsDict.TryGetValue(rawActor.Name, out var actor))
                {
                    actor = new Actor { Name = rawActor.Name };
                    actorsDict[rawActor.Name] = actor;
                    await dbContext.Actors.AddAsync(actor);
                }

                var movieActor = new MovieActor
                {
                    Movie = movie,
                    Actor = actor,
                    CharacterName = rawActor.Character
                };
                await dbContext.MovieActors.AddAsync(movieActor);
            }
        }

        await dbContext.SaveChangesAsync();

        return Results.Ok(new 
        { 
            message = "Successfully seeded normalized tables.", 
            movies_count = rawMovies.Count,
            directors_count = directorsDict.Count,
            actors_count = actorsDict.Count,
            genres_count = genresDict.Count
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Seeding failed: {ex.Message}");
    }
});

// Endpoint to clear DB
app.MapPost("/api/db/clear", async (AppDbContext dbContext) =>
{
    dbContext.MovieActors.RemoveRange(dbContext.MovieActors);
    dbContext.Movies.RemoveRange(dbContext.Movies);
    dbContext.Directors.RemoveRange(dbContext.Directors);
    dbContext.Actors.RemoveRange(dbContext.Actors);
    dbContext.Genres.RemoveRange(dbContext.Genres);
    await dbContext.SaveChangesAsync();
    return Results.Ok(new { message = "Normalized database cleared." });
});

// ==========================================
// OpenSearch Endpoints (Disabled temporarily for Phase 1 verification)
// ==========================================

app.MapPost("/api/search/opensearch/sync", () => Results.Problem("Sync disabled during database refactoring. Please verify Phase 1 first."));
app.MapGet("/api/search/opensearch", () => Results.Problem("Search disabled during database refactoring. Please verify Phase 1 first."));

app.Run();

// Helper models for JSON deserialization
public class RawMovie
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string Plot { get; set; } = null!;
    
    [JsonPropertyName("release_year")]
    public int ReleaseYear { get; set; }
    
    public double Rating { get; set; }
    public List<string> Genres { get; set; } = new();
    public string Director { get; set; } = null!;
    public List<RawActor> Actors { get; set; } = new();
}

public class RawActor
{
    public string Name { get; set; } = null!;
    public string Character { get; set; } = null!;
}
