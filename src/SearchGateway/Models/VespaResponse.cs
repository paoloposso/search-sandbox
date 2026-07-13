using System.Text.Json.Serialization;

namespace SearchGateway.Models;

public class VespaResponse
{
    [JsonPropertyName("root")]
    public VespaRoot Root { get; set; } = new();
}

public class VespaRoot
{
    [JsonPropertyName("fields")]
    public VespaRootFields Fields { get; set; } = new();

    [JsonPropertyName("children")]
    public List<VespaChild>? Children { get; set; }
}

public class VespaRootFields
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }
}

public class VespaChild
{
    [JsonPropertyName("relevance")]
    public double Relevance { get; set; }

    [JsonPropertyName("fields")]
    public MovieSearchDocument Fields { get; set; } = null!;
}
