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
                    Message = $"Successfully synchronized {movies.Count} movies to OpenSearch.",
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

        // Delete OpenSearch index
        group.MapDelete("/opensearch/clear", async (OpenSearchService openSearchService) =>
        {
            try
            {
                var response = await openSearchService.DeleteIndexAsync();
                if (!response.IsValid && response.ServerError?.Status != 404)
                {
                    return Results.Problem($"Failed to delete index: {response.ServerError?.Error?.Reason ?? response.OriginalException?.Message}");
                }
                return Results.Ok(new { message = "OpenSearch index 'movies' deleted successfully." });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Clear failed: {ex.Message}");
            }
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Sync movies from Postgres to Vespa
        group.MapPost("/vespa/sync", async (AppDbContext dbContext, VespaService vespaService) =>
        {
            try
            {
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

                await vespaService.SyncMoviesAsync(searchDocs);

                return Results.Ok(new { message = $"Successfully synchronized {movies.Count} movies to Vespa." });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Vespa sync failed: {ex.Message}");
            }
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Delete Vespa documents
        group.MapDelete("/vespa/clear", async (VespaService vespaService) =>
        {
            try
            {
                await vespaService.DeleteAllDocumentsAsync();
                return Results.Ok(new { message = "Vespa documents cleared successfully." });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Clear failed: {ex.Message}");
            }
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Query OpenSearch across denormalized properties (Title, Plot, Director, Actors)
        group.MapGet("/opensearch", async (string? q, string? genre, string? type, int? page, int? pageSize, OpenSearchService openSearchService) =>
        {
            if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(genre))
            {
                return Results.BadRequest("At least one search parameter ('q' or 'genre') is required.");
            }

            try
            {
                var p = page ?? 1;
                var ps = pageSize ?? 10;
                var searchResponse = await openSearchService.SearchAsync(q, genre, type, p, ps);
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

        // Query Vespa search API
        group.MapGet("/vespa", async (string? q, string? genre, string? type, int? page, int? pageSize, VespaService vespaService) =>
        {
            if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(genre))
            {
                return Results.BadRequest("At least one search parameter ('q' or 'genre') is required.");
            }

            try
            {
                var p = page ?? 1;
                var ps = pageSize ?? 10;
                var searchResponse = await vespaService.SearchAsync(q, genre, p, ps);
                if (searchResponse == null)
                {
                    return Results.Problem("No response from Vespa search service.");
                }

                var totalCount = searchResponse.Root.Fields.TotalCount;
                var results = searchResponse.Root.Children?.Select(child => new MovieSearchResultItem
                {
                    Score = child.Relevance,
                    Id = child.Fields.Id,
                    Title = child.Fields.Title,
                    Plot = child.Fields.Plot,
                    ReleaseYear = child.Fields.ReleaseYear,
                    Rating = child.Fields.Rating,
                    DirectorName = child.Fields.DirectorName,
                    Genres = child.Fields.Genres,
                    ActorNames = child.Fields.ActorNames
                }) ?? Enumerable.Empty<MovieSearchResultItem>();

                return Results.Ok(new OpenSearchQueryResponse
                {
                    Total = totalCount,
                    Results = results
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Vespa search failed: {ex.Message}");
            }
        })
        .Produces<OpenSearchQueryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Benchmark both search engines concurrently
        group.MapGet("/compare", async (string? q, string? genre, string? type, int? page, int? pageSize, OpenSearchService openSearchService, VespaService vespaService) =>
        {
            if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(genre))
            {
                return Results.BadRequest("At least one search parameter ('q' or 'genre') is required.");
            }

            try
            {
                var p = page ?? 1;
                var ps = pageSize ?? 10;

                // Query OpenSearch and measure latency
                var osStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var osTask = openSearchService.SearchAsync(q, genre, type, p, ps);

                // Query Vespa and measure latency
                var vespaStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var vespaTask = vespaService.SearchAsync(q, genre, p, ps);

                await Task.WhenAll(osTask, vespaTask);

                osStopwatch.Stop();
                var osResponse = await osTask;

                vespaStopwatch.Stop();
                var vespaResponse = await vespaTask;

                var osResults = osResponse.Hits.Select(hit => new MovieSearchResultItem
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

                var osResultPayload = new CompareSearchEngineResult
                {
                    LatencyMs = osStopwatch.ElapsedMilliseconds,
                    Total = osResponse.Total,
                    Results = osResults
                };

                var vespaResults = vespaResponse?.Root.Children?.Select(child => new MovieSearchResultItem
                {
                    Score = child.Relevance,
                    Id = child.Fields.Id,
                    Title = child.Fields.Title,
                    Plot = child.Fields.Plot,
                    ReleaseYear = child.Fields.ReleaseYear,
                    Rating = child.Fields.Rating,
                    DirectorName = child.Fields.DirectorName,
                    Genres = child.Fields.Genres,
                    ActorNames = child.Fields.ActorNames
                }) ?? Enumerable.Empty<MovieSearchResultItem>();

                var vespaResultPayload = new CompareSearchEngineResult
                {
                    LatencyMs = vespaStopwatch.ElapsedMilliseconds,
                    Total = vespaResponse?.Root.Fields.TotalCount ?? 0,
                    Results = vespaResults
                };

                return Results.Ok(new CompareResponse
                {
                    OpenSearch = osResultPayload,
                    Vespa = vespaResultPayload
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Comparison query failed: {ex.Message}");
            }
        })
        .Produces<CompareResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}

