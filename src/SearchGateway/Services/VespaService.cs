using System.Net.Http.Json;
using SearchGateway.Models;

namespace SearchGateway.Services;

public class VespaService(HttpClient httpClient)
{
    public async Task<VespaResponse?> SearchAsync(
        string? queryText, string? genre, string? searchType = null, int page = 1, int pageSize = 10)
    {
        var offset = (page - 1) * pageSize;
        var hits = pageSize;

        // Build YQL Query
        string yql;
        if (!string.IsNullOrWhiteSpace(queryText) && !string.IsNullOrWhiteSpace(genre))
        {
            yql = $"select * from movie where userQuery() and genres contains \"{genre}\"";
        }
        else if (!string.IsNullOrWhiteSpace(queryText))
        {
            yql = "select * from movie where userQuery()";
        }
        else if (!string.IsNullOrWhiteSpace(genre))
        {
            yql = $"select * from movie where genres contains \"{genre}\"";
        }
        else
        {
            yql = "select * from movie where true";
        }

        // Build request parameters
        var queryParams = new Dictionary<string, string?>
        {
            { "yql", yql },
            { "offset", offset.ToString() },
            { "hits", hits.ToString() },
            { "ranking", "movie_rank" }
        };

        if (!string.IsNullOrWhiteSpace(queryText))
        {
            queryParams.Add("query", queryText);
        }

        // Use relative search path
        var uri = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("search/", queryParams);

        try
        {
            var response = await httpClient.GetAsync(uri);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<VespaResponse>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling Vespa search API: {ex.Message}");
            throw;
        }
    }

    public async Task SyncMoviesAsync(IEnumerable<MovieSearchDocument> documents)
    {
        // Vespa document feed relative endpoint
        var options = new ParallelOptions { MaxDegreeOfParallelism = 10 };
        await Parallel.ForEachAsync(documents, options, async (doc, cancellationToken) =>
        {
            var feedDoc = new { fields = doc };
            var url = $"document/v1/movie/movie/docid/{doc.Id}";
            var response = await httpClient.PostAsJsonAsync(url, feedDoc, cancellationToken);
            response.EnsureSuccessStatusCode();
        });
    }

    public async Task DeleteAllDocumentsAsync()
    {
        // Vespa document delete relative endpoint
        var url = "document/v1/movie/movie/docid/?selection=true";
        var response = await httpClient.DeleteAsync(url);
        response.EnsureSuccessStatusCode();
    }
}
