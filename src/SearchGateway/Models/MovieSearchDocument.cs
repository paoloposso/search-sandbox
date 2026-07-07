using System.Text.Json.Serialization;

namespace SearchGateway.Models;

public class MovieSearchDocument
{
    public int Id { get; set; }

    public required string Title { get; set; }

    public required string Plot { get; set; }

    [JsonPropertyName("release_year")]
    public int ReleaseYear { get; set; }

    public double Rating { get; set; }

    [JsonPropertyName("director_name")]
    public required string DirectorName { get; set; }

    public required IEnumerable<string> Genres { get; set; }

    [JsonPropertyName("actor_names")]
    public required IEnumerable<string> ActorNames { get; set; }
}
