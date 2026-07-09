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
            .Map<MovieSearchDocument>(m => m
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
                    .Text(t => t
                        .Name(f => f.DirectorName)
                        .Analyzer("english")
                    )
                    .Text(t => t
                        .Name(f => f.ActorNames)
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

    public async Task<BulkResponse> SyncMoviesAsync(IEnumerable<MovieSearchDocument> documents)
    {
        await EnsureIndexExistsAsync();

        // Use custom bulk mapping to ensure documents are indexed under correct IDs
        var response = await client.BulkAsync(b => b
            .Index(IndexName)
            .IndexMany(documents, (descriptor, doc) => descriptor.Id(doc.Id.ToString()))
        );

        return response;
    }

    public async Task<ISearchResponse<MovieSearchDocument>> SearchAsync(
        string? queryText, string? genre, string? searchType = "multi")
    {
        await EnsureIndexExistsAsync();

        return await client.SearchAsync<MovieSearchDocument>(s => s
            .Index(IndexName)
            .Query(q => q
                .Bool(b => b
                    .Must(m => 
                    {
                        if (!string.IsNullOrWhiteSpace(queryText))
                        {
                            if (searchType == "simple")
                            {
                                return m.SimpleQueryString(sqs => sqs
                                    .Fields(f => f
                                        .Field(movie => movie.Title, boost: 2.0)
                                        .Field(movie => movie.Plot)
                                        .Field(movie => movie.DirectorName, boost: 1.5)
                                        .Field(movie => movie.ActorNames, boost: 1.5)
                                        .Field(movie => movie.Genres, boost: 1.5)
                                    )
                                    .Query(queryText)
                                );
                            }
                            else
                            {
                                return m.MultiMatch(mm => mm
                                    .Fields(f => f
                                        .Field(movie => movie.Title, boost: 2.0)
                                        .Field(movie => movie.Plot)
                                        .Field(movie => movie.DirectorName, boost: 1.5)
                                        .Field(movie => movie.ActorNames, boost: 1.5)
                                        .Field(movie => movie.Genres, boost: 1.5)
                                    )
                                    .Query(queryText)
                                    .Fuzziness(Fuzziness.Auto)
                                );
                            }
                        }
                        return m.MatchAll();
                    })
                    .Filter(f =>
                    {
                        var filterList =
                            new List<Func<QueryContainerDescriptor<MovieSearchDocument>, QueryContainer>>();

                        if (!string.IsNullOrWhiteSpace(genre))
                        {
                            filterList.Add(fq => fq.Term(t => t.Field(m => m.Genres).Value(genre)));
                        }

                        return f.Bool(fb => fb.Must(filterList));
                    })
                )
            )
        );
    }
}
