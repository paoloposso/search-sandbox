using Microsoft.AspNetCore.Http;
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

                return Results.Ok(new SyncResponse
                {
                    Message = $"Successfully synchronized {movies.Count} denormalized movies to OpenSearch.",
                    TookMs = bulkResponse.Took,
                    HasErrors = bulkResponse.Errors
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Sync failed: {ex.Message}");
            }
        })
        .Produces<SyncResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Query OpenSearch across denormalized properties (Title, Plot, Director, Actors)
        group.MapGet("/opensearch", async (string? q, string? genre, string? type, OpenSearchService openSearchService) =>
        {
            if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(genre))
            {
                return Results.BadRequest("At least one search parameter ('q' or 'genre') is required.");
            }

            try
            {
                var searchResponse = await openSearchService.SearchAsync(q, genre, type);
                if (!searchResponse.IsValid)
                {
                    return Results.Problem($"Search query failed: {searchResponse.ServerError?.Error?.Reason}");
                }

                var results = searchResponse.Hits.Select(hit => new MovieSearchResultItem
                {
                    Score = hit.Score,
                    Id = hit.Source.Id,
                    Title = hit.Source.Title,
                    Plot = hit.Source.Plot,
                    ReleaseYear = hit.Source.ReleaseYear,
                    Rating = hit.Source.Rating,
                    DirectorName = hit.Source.DirectorName,
                    Genres = hit.Source.Genres,
                    ActorNames = hit.Source.ActorNames
                });

                return Results.Ok(new OpenSearchQueryResponse
                {
                    Total = searchResponse.Total,
                    Results = results
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Search failed: {ex.Message}");
            }
        })
        .Produces<OpenSearchQueryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}

