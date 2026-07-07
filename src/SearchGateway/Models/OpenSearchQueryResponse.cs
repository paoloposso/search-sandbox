using System.Text.Json.Serialization;

namespace SearchGateway.Models;

public class MovieSearchResultItem
{
    public double? Score { get; set; }
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Plot { get; set; }

    [JsonPropertyName("release_year")]
    public int ReleaseYear { get; set; }

    public double Rating { get; set; }

    [JsonPropertyName("director_name")]
    public required string DirectorName { get; set; }

    public IEnumerable<string> Genres { get; set; } = [];

    [JsonPropertyName("actor_names")]
    public IEnumerable<string> ActorNames { get; set; } = [];
}

public class OpenSearchQueryResponse
{
    public long Total { get; set; }
    public IEnumerable<MovieSearchResultItem> Results { get; set; } = [];
}
