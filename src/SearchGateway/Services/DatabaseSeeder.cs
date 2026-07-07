using System.Text.Json;
using SearchGateway.Data;
using SearchGateway.Models;

namespace SearchGateway.Services;

public class DatabaseSeeder(AppDbContext dbContext)
{
    public async Task<SeedingResult> SeedAsync()
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

            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                return SeedingResult.Failure("Could not find movies_mock_data.json in any parent directory.");
            }

            var jsonString = await File.ReadAllTextAsync(jsonPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rawMovies = JsonSerializer.Deserialize<List<RawMovie>>(jsonString, options);

            if (rawMovies is null || rawMovies.Count == 0)
            {
                return SeedingResult.Failure("No movies found in JSON dataset.");
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

            return SeedingResult.SuccessResult(
                rawMovies.Count,
                directorsDict.Count,
                actorsDict.Count,
                genresDict.Count
            );
        }
        catch (Exception ex)
        {
            return SeedingResult.Failure($"Seeding failed: {ex.Message}");
        }
    }
}

public class SeedingResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int MoviesCount { get; set; }
    public int DirectorsCount { get; set; }
    public int ActorsCount { get; set; }
    public int GenresCount { get; set; }

    public static SeedingResult Failure(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };

    public static SeedingResult SuccessResult(int movies, int directors, int actors, int genres) => new()
    {
        Success = true,
        MoviesCount = movies,
        DirectorsCount = directors,
        ActorsCount = actors,
        GenresCount = genres
    };
}
