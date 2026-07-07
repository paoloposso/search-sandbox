using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SearchGateway.Models;

public class Movie
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(500)]
    public required string Title { get; set; }

    [Required]
    public required string Plot { get; set; }

    [JsonPropertyName("release_year")]
    public int ReleaseYear { get; set; }

    public double Rating { get; set; }

    // Relationship to Director (Many Movies to One Director)
    public int DirectorId { get; set; }
    
    public required Director Director { get; set; }

    // Relationship to Actors (Many-to-Many with Payload)
    public List<MovieActor> MovieActors { get; set; } = new();

    // Relationship to Genres (Many-to-Many implicit join table)
    public List<Genre> Genres { get; set; } = new();
}
