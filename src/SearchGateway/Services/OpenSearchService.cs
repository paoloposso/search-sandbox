using OpenSearch.Client;
using SearchGateway.Models;

namespace SearchGateway.Services;

public class OpenSearchService(IOpenSearchClient client)
{
    private const string IndexName = "movies";

    public async Task EnsureIndexExistsAsync()
    {
        var existsResponse = await client.Indices.ExistsAsync(IndexName);
        if (existsResponse.Exists)
        {
            return;
        }

        var createResponse = await client.Indices.CreateAsync(IndexName, c => c
            .Settings(s => s
                .NumberOfShards(1)
                .NumberOfReplicas(0)
            )
            .Map<Movie>(m => m
                .AutoMap()
                .Properties(p => p
                    .Number(n => n
                        .Name(f => f.Id)
                        .Type(NumberType.Integer)
                    )
                    .Text(t => t
                        .Name(f => f.Title)
                        .Analyzer("english")
                        .Fields(fields => fields
                            .Keyword(k => k.Name("keyword"))
                        )
                    )
                    .Text(t => t
                        .Name(f => f.Plot)
                        .Analyzer("english")
                    )
                    .Number(n => n
                        .Name(f => f.ReleaseYear)
                        .Type(NumberType.Integer)
                    )
                    .Number(n => n
                        .Name(f => f.Rating)
                        .Type(NumberType.Double)
                    )
                    .Keyword(k => k
                        .Name(f => f.Genres)
                    )
                )
            )
        );

        if (!createResponse.IsValid)
        {
            throw new Exception($"Failed to create OpenSearch index: {createResponse.ServerError?.Error?.Reason}");
        }
    }

    public async Task<BulkResponse> SyncMoviesAsync(List<Movie> movies)
    {
        await EnsureIndexExistsAsync();

        // Use custom bulk mapping to ensure documents are indexed under correct IDs
        var response = await client.BulkAsync(b => b
            .Index(IndexName)
            .IndexMany(movies, (descriptor, movie) => descriptor.Id(movie.Id.ToString()))
        );

        return response;
    }

    public async Task<ISearchResponse<Movie>> SearchAsync(string queryText)
    {
        await EnsureIndexExistsAsync();

        var response = await client.SearchAsync<Movie>(s => s
            .Index(IndexName)
            .Query(q => q
                .MultiMatch(mm => mm
                    .Fields(f => f
                        .Field(m => m.Title, boost: 2.0)
                        .Field(m => m.Plot)
                    )
                    .Query(queryText)
                    .Fuzziness(Fuzziness.Auto)
                )
            )
        );

        return response;
    }
}
