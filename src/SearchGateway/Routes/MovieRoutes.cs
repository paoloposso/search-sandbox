using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using SearchGateway.Data;
using SearchGateway.Models;

namespace SearchGateway.Routes;

public static class MovieRoutes
{
    public static void MapMovieEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // Retrieve movies directly from Postgres (Eager Loading relations)
        group.MapGet("/movies", async (AppDbContext dbContext) =>
        {
            var movies = await dbContext.Movies
                .Include(m => m.Director)
                .Include(m => m.Genres)
                .Include(m => m.MovieActors)
                    .ThenInclude(ma => ma.Actor)
                .ToListAsync();

            return Results.Ok(movies);
        })
        .Produces<IEnumerable<Movie>>(StatusCodes.Status200OK);
    }
}
