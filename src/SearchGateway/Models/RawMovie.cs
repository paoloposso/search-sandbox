using System.Text.Json.Serialization;

namespace SearchGateway.Models;

public class RawMovie
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string Plot { get; set; } = null!;

    [JsonPropertyName("release_year")]
    public int ReleaseYear { get; set; }

    public double Rating { get; set; }
    public List<string> Genres { get; set; } = new();
    public string Director { get; set; } = null!;
    public List<RawActor> Actors { get; set; } = new();
}
