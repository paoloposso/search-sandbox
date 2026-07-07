using Microsoft.EntityFrameworkCore;
using SearchGateway.Data;
using SearchGateway.Models;
using SearchGateway.Services;

namespace SearchGateway.Routes;

public static class SearchRoutes
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/search");

        // Sync movies from Postgres to OpenSearch (with Denormalization mapping)
        group.MapPost("/opensearch/sync", async (AppDbContext dbContext, OpenSearchService openSearchService) =>
        {
            try
            {
                // Eager-load the relational graph from PostgreSQL
                var movies = await dbContext.Movies
                    .Include(m => m.Director)
                    .Include(m => m.Genres)
                    .Include(m => m.MovieActors)
                        .ThenInclude(ma => ma.Actor)
                    .ToListAsync();

                if (movies.Count == 0)
                {
                    return Results.BadRequest("No movies in database to sync. Please run seeding first.");
                }

                // Map fully normalized database entities into flat, denormalized OpenSearch documents
                var searchDocs = movies.Select(m => new MovieSearchDocument
                {
                    Id = m.Id,
                    Title = m.Title,
                    Plot = m.Plot,
                    ReleaseYear = m.ReleaseYear,
                    Rating = m.Rating,
                    DirectorName = m.Director.Name,
                    Genres = m.Genres.Select(g => g.Name),
                    ActorNames = m.MovieActors.Select(ma => ma.Actor.Name)
                });

                var bulkResponse = await openSearchService.SyncMoviesAsync(searchDocs);
                if (!bulkResponse.IsValid)
                {
                    return Results.Problem($"Bulk sync failed: {bulkResponse.ServerError?.Error?.Reason ?? bulkResponse.OriginalException?.Message}");
                }

                return Results.Ok(new
                {
                    message = $"Successfully synchronized {movies.Count} denormalized movies to OpenSearch.",
                    took_ms = bulkResponse.Took,
                    has_errors = bulkResponse.Errors
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Sync failed: {ex.Message}");
            }
        });

        // Query OpenSearch across denormalized properties (Title, Plot, Director, Actors)
        group.MapGet("/opensearch", async (string q, OpenSearchService openSearchService) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.BadRequest("Query parameter 'q' is required.");
            }

            try
            {
                var searchResponse = await openSearchService.SearchAsync(q);
                if (!searchResponse.IsValid)
                {
                    return Results.Problem($"Search query failed: {searchResponse.ServerError?.Error?.Reason}");
                }

                var results = searchResponse.Hits.Select(hit => new
                {
                    score = hit.Score,
                    id = hit.Source.Id,
                    title = hit.Source.Title,
                    plot = hit.Source.Plot,
                    release_year = hit.Source.ReleaseYear,
                    rating = hit.Source.Rating,
                    director_name = hit.Source.DirectorName,
                    genres = hit.Source.Genres,
                    actor_names = hit.Source.ActorNames
                });

                return Results.Ok(new
                {
                    total = searchResponse.Total,
                    results
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Search failed: {ex.Message}");
            }
        });
    }
}
