namespace SearchGateway.Models;

public class CompareResponse
{
    public required CompareSearchEngineResult OpenSearch { get; set; }
    public required CompareSearchEngineResult Vespa { get; set; }
}

public class CompareSearchEngineResult
{
    public long LatencyMs { get; set; }
    public long Total { get; set; }
    public required IEnumerable<MovieSearchResultItem> Results { get; set; }
}
