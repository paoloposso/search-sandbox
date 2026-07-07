using Microsoft.EntityFrameworkCore;
using SearchGateway.Models;

namespace SearchGateway.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Director> Directors => Set<Director>();
    public DbSet<Actor> Actors => Set<Actor>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<MovieActor> MovieActors => Set<MovieActor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Define composite key for many-to-many MovieActor table
        modelBuilder.Entity<MovieActor>()
            .HasKey(ma => new { ma.MovieId, ma.ActorId });

        // Configure relationships for MovieActor
        modelBuilder.Entity<MovieActor>()
            .HasOne(ma => ma.Movie)
            .WithMany(m => m.MovieActors)
            .HasForeignKey(ma => ma.MovieId);

        modelBuilder.Entity<MovieActor>()
            .HasOne(ma => ma.Actor)
            .WithMany(a => a.MovieActors)
            .HasForeignKey(ma => ma.ActorId);

        // Configure index for movie title
        modelBuilder.Entity<Movie>()
            .HasIndex(m => m.Title);

        // Configure index for actor name
        modelBuilder.Entity<Actor>()
            .HasIndex(a => a.Name);

        // Configure index for director name
        modelBuilder.Entity<Director>()
            .HasIndex(d => d.Name);
    }
}
