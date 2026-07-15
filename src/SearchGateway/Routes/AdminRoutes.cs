using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using SearchGateway.Data;
using SearchGateway.Models;
using SearchGateway.Services;

namespace SearchGateway.Routes;

public static class AdminRoutes
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // Seed normalized movies into Postgres from JSON file
        group.MapPost("/db/seed", async (DatabaseSeeder seeder) =>
        {
            var result = await seeder.SeedAsync();
            return result.Success
                ? Results.Ok(result)
                : Results.Problem(result.ErrorMessage);
        })
        .Produces<SeedingResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Clear DB
        group.MapPost("/db/clear", async (AppDbContext dbContext) =>
        {
            dbContext.MovieActors.RemoveRange(dbContext.MovieActors);
            dbContext.Movies.RemoveRange(dbContext.Movies);
            dbContext.Directors.RemoveRange(dbContext.Directors);
            dbContext.Actors.RemoveRange(dbContext.Actors);
            dbContext.Genres.RemoveRange(dbContext.Genres);
            await dbContext.SaveChangesAsync();
            return Results.Ok(new { message = "Normalized database cleared." });
        })
        .Produces(StatusCodes.Status200OK);
    }
}
