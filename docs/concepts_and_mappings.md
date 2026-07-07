# Concepts & Search Index Structures

This document explains the conceptual differences between relational databases and search engines, and details how our movie data is structured in both.

---

## 1. Relational Databases vs. Search Engines

When building search-heavy applications, it is standard practice to separate the **Transactional Database** (System of Record) from the **Search Engine** (Read-Only Views).

| Concept | PostgreSQL (Relational DB) | OpenSearch / Vespa (Search Engine) |
| :--- | :--- | :--- |
| **Data Container** | **Table**: Structured columns and rows. | **Index**: Flat collection of JSON documents. |
| **Data Unit** | **Row / Record**: Represents a normalized entity. | **Document**: Represents a self-contained, denormalized record. |
| **Schema** | **Strict**: Rigid columns, types, and foreign key relations. | **Flexible/Semi-structured**: JSON fields, easily nested arrays. |
| **Primary Indexing** | **B-Tree**: Designed for fast exact matches (`WHERE id = 5`), range queries, and ACID transactions. | **Inverted Index & Vector Index**: Designed for scoring text matching relevance (e.g. TF-IDF, BM25) and semantic vectors. |
| **Querying** | **SQL**: Structured Query Language. | **DSL / YQL**: Domain Specific Languages optimized for relevance matching. |

### Why copy data from Postgres to OpenSearch?
PostgreSQL is excellent for transactional consistency (writing reviews, updating prices, user authentication). However, executing `LIKE %cyberpunk%` on a long text column in Postgres requires scanning every single row, which is extremely slow and lacks **relevance scoring** (ranking how "close" a document is to the search query). 

OpenSearch indexes the text into an **Inverted Index** (mapping words to the documents they appear in), making full-text searches near-instantaneous.

---

## 2. OpenSearch Index Structure (`movies`)

In OpenSearch, our index is defined with specific field mappings. Mappings define how each field in our JSON document is stored and analyzed.

```json
{
  "mappings": {
    "properties": {
      "id": {
        "type": "integer"
      },
      "title": {
        "type": "text",
        "analyzer": "english",
        "fields": {
          "keyword": {
            "type": "keyword"
          }
        }
      },
      "plot": {
        "type": "text",
        "analyzer": "english"
      },
      "release_year": {
        "type": "integer"
      },
      "rating": {
        "type": "double"
      },
      "genres": {
        "type": "keyword"
      }
    }
  }
}
```

### Explaining the Fields:

*   **`text` vs `keyword` types**:
    *   **`text` fields** (`title`, `plot`) are analyzed. This means sentences are broken into lowercase words, suffixes are stripped (e.g. "hackers" becomes "hacker"), and common words like "the" are ignored. This allows matching queries like `"hacker"` against plots containing `"hacking"`.
    *   **`keyword` fields** (`genres`, `title.keyword`) are stored as exact strings. They are not tokenized. This is ideal for filters (e.g. filtering exactly by genre `"Sci-Fi"`) or aggregations.
*   **Linguistic Analyzers (`analyzer: "english"`)**:
    We configured the `english` analyzer. When syncing "The Matrix", its plot contains:
    > "...Mysterious rebels about the true nature of his reality..."
    
    The analyzer converts this in the index to terms: `[mysteri, rebel, true, natur, realiti]`. When a user searches for `"rebellion"`, it matches `"rebel"` via stemming!

---

## 3. SQL Normalization vs. Search Denormalization

You are exactly right! In this basic POC, the SQL table and the OpenSearch index have a similar structure. However, in real-world systems, search indices look drastically different from SQL tables.

### The Denormalization Principle
In relational databases, we **normalize** data to prevent redundancy. We split data into multiple tables and link them with Foreign Keys:
- `Movies` table
- `Actors` table
- `Reviews` table
- `MovieActors` (join table)

In search engines, **joins do not exist or are extremely slow**. Instead, we **denormalize** the data. When syncing to OpenSearch, we query the SQL database, join all the related tables, and write a single, self-contained JSON document:

#### Normalized Postgres Database Structure:
```
[Movies Table] --< [MovieActors] >-- [Actors Table]
  - Id                                 - Id
  - Title                              - Name
```

#### Denormalized OpenSearch Document (`movies` index):
```json
{
  "id": 1,
  "title": "The Matrix",
  "rating": 8.7,
  "actors": [
    { "id": 101, "name": "Keanu Reeves", "role": "Neo" },
    { "id": 102, "name": "Laurence Fishburne", "role": "Morpheus" }
  ],
  "director": {
    "id": 50,
    "name": "Lana Wachowski"
  }
}
```

### When do we create a separate index?
We create separate indexes only when we want to search for a **different main entity**. 
- If you have a search bar for **Movies**, you query the `movies` index (which has actors embedded in it).
- If your app has an **Actor Profiles Directory** (where users search specifically for actors to see their biographies and birthdays), you would create a separate `actors` index.

### Pulling from Multiple Data Sources (SQL + Key-Value + APIs)
Search engines allow you to aggregate data from completely different systems into one searchable JSON document. 
For example, you could fetch the core metadata from **Postgres**, real-time inventory or click-counts from **Redis (Key-Value)**, and external review scores from a **third-party API**, merging them together before sending the final document to OpenSearch:

```
[Postgres (Metadata)]  ----\
[Redis (Views/Clicks)]  ----> [Ingestion Service] ---> [OpenSearch (Single Document)]
[Third-Party Review API] --/
```
This is the power of Search Indices: they are custom-tailored search views designed to serve a specific UI feature, independent of how the transactional data is stored under the hood.

---

## 4. OpenSearch Persistence & Synchronization Patterns

### How OpenSearch Stores Data & Persistence
OpenSearch is built on Apache Lucene. When a write (index/update/delete) request occurs:

1.  **Indexing Buffer & Translog**: The document is written to an in-memory **Indexing Buffer** and simultaneously appended to a write-ahead log on disk called the **Translog** (Transaction Log).
2.  **Refresh (Near Real-Time)**: Every second (by default), the indexing buffer is written into a new immutable Lucene **segment file** on disk. Once in a segment, the document becomes searchable.
3.  **Translog Commit**: The Translog is fsynced to disk regularly (default is every request or every 5 seconds) to guarantee durability. If the container crashes, OpenSearch replays the Translog on startup to restore uncommitted data.
4.  **Immutability**: Lucene segments are immutable. Updates are handled by marking the old document as deleted in a `.del` file and writing a new document. In the background, segment merges clean up deleted entries.

While persistent, OpenSearch is **not** designed for transactional database guarantees (no rollbacks, no relational integrity constraint checks). It is a secondary index.

### Synchronization Patterns: Database $\rightarrow$ Search Index

How do we propagate changes from Postgres to OpenSearch?

#### A. Dual Write (Write-Through)
The application code performs the write to Postgres, and in the same request, maps and writes to OpenSearch.
*   **Pros**: Instant updates in search.
*   **Cons**: Lack of transaction safety. If OpenSearch is down or slow, the Postgres transaction might succeed but the index goes out of sync (data drift).

#### B. Transactional Outbox Pattern (Recommended for microservices)
The application writes the database change and an event record (e.g. `MovieUpdated`) into an `Outbox` table in Postgres in the same atomic database transaction.
*   A background **worker** service reads new messages from the `Outbox` table, reads the joined data from Postgres, and pushes the denormalized document to OpenSearch.
*   **Pros**: Guarantees eventual consistency. Even if OpenSearch goes down, the worker will retry once it is back online.

#### C. Change Data Capture (CDC)
A tool like **Debezium** reads Postgres's Write-Ahead Log (WAL) directly, pushes changes to Kafka, and an ingestion consumer updates OpenSearch.
*   **Pros**: Zero impact on application code, highly decoupled.
*   **Cons**: Requires dedicated infrastructure (Kafka, Debezium, ZooKeeper).

#### D. Can we use "Cache-Miss" (Lazy Loading)?
**No**. Unlike a Key-Value cache (like Redis), you **cannot** lazy-load a search index.
If a document is missing in a key-value store, you can query by ID from SQL and load it on demand. But in a search engine, if a document is missing, the search engine doesn't know it's missing—it simply returns fewer search results. It cannot run a full-text query like `"90s action"` against Postgres to find the missing rows on the fly. 
Therefore, search engine indexes must **always be eagerly synchronized and fully populated** to return correct search results.

---

## 5. Querying Specific Fields in OpenSearch

We are currently using a **`MultiMatch`** query, which behaves like a unified search box by searching a single keyword `q` across multiple mapped text fields. 

However, OpenSearch allows you to target specific fields, filter values exactly, and combine conditions.

### A. Targeting a Single Field (`Match`)
If you want to build a search feature specifically for searching directors, you query *only* the `director_name` field:
```csharp
q => q.Match(m => m
    .Field(f => f.DirectorName)
    .Query(queryText)
)
```

### B. Exact Matching (`Term` Queries)
For non-analyzed fields (like numbers or `keyword` fields), we use `Term` queries. This avoids text analysis and performs an exact lookup:
```csharp
q => q.Term(t => t
    .Field(f => f.Genres)
    .Value("Sci-Fi")
)
```

### C. Combining Conditions (`Bool` Queries)
In real applications, you often combine full-text queries with filters (e.g. searching for `"Joker"` in `title` AND filtering where `release_year` > `2000` AND `genres` = `"Drama"`). 

We do this using a **`bool`** query which supports:
- `must`: Conditions that *must* be true and contribute to relevance scoring.
- `filter`: Conditions that *must* be true but run faster because they bypass scoring (caching results).
- `should`: Optional matches that boost the score if present.

Example C# builder:
```csharp
q => q.Bool(b => b
    .Must(mu => mu
        .Match(m => m
            .Field(f => f.Title)
            .Query("Joker")
        )
    )
    .Filter(
        fi => fi.Term(t => t.Field(f => f.Genres).Value("Drama")),
        fi => fi.Range(r => r.Field(f => f.ReleaseYear).GreaterThan(2000))
    )
)
```

Since `Title` has a boost of `2.0`, Movie A's matching score will be doubled, ensuring it ranks significantly higher in the search results than Movie B, even though both contain the word `"speed"`.

---

## 6. Advanced Query Patterns in Production

### A. Thresholding & Score Cutoffs
In your search query for `"Matrix"`, you noticed fuzzy matches (like *Goodfellas* or *Spirited Away*) appearing with low scores. In production, we filter these out using two main techniques:

1.  **`MinScore` Threshold**: We can configure OpenSearch to reject any hit with a score lower than a specific value (e.g. `min_score: 2.0`). This removes low-relevance fuzzy noise.
2.  **Pagination (Top-N sorting)**: Because OpenSearch automatically sorts results by `_score` descending, the most relevant hits are placed on Page 1. Users rarely browse past the first page, rendering low-scoring noise harmless.

### B. Hybrid Search: Strict Filters + Free Text (Faceted Search)
It is extremely common (and expected) to combine **strict exact filters** (like categories, stock availability, or price ranges) with **free-text search** (like titles or descriptions). 

This is called **Faceted Search** or **Hybrid Querying** (commonly seen in e-commerce filters).

We implement this in OpenSearch using a `bool` query:
- **`must` (Free Text)**: The user's input searches the analyzed text fields (`title`, `plot`) with fuzziness enabled.
- **`filter` (Strict)**: The selected categories are matched exactly against non-analyzed `keyword` fields using `term` queries. Filters bypass relevance scoring and are cached, making them extremely fast.

```
       [ User searches: "spaceship" ]           [ User filters: Genre = "Sci-Fi" ]
                   │                                          │
                   ▼                                          ▼
  ┌─────────────────────────────────┐        ┌─────────────────────────────────┐
  │      Free-Text (must match)     │        │     Strict Filter (yes/no gate)  │
  │  - Fuzzy search on 'title/plot' │        │  - Exact 'term' query on 'genre'│
  │  - Scores relevance (BM25)      │        │  - Bypasses scoring (fast cache)│
  └────────────────┬────────────────┘        └────────────────┬────────────────┘
                   │                                          │
                   └───────────────────► Bool ◄───────────────┘
                                         Query
```
This ensures the search is highly flexible for general keywords, but strictly restricted to the selected catalog categories.

---

## 7. Understanding BM25 Relevance Scoring & Boosting Math

When you query OpenSearch, it returns a `score` for each document (e.g. `2.7676687` for `"sci"`). These scores are calculated using the **Okapi BM25** algorithm, which is the industry standard for keyword search.

### A. The BM25 Scoring Formula (Simplified)
BM25 evaluates three core criteria for each field that matches your query:

1.  **Term Frequency (TF)**: How many times does the search term appear in this field for this document?
    *   *More matches = higher score.* (e.g. if a movie plot mentions "sci-fi" 3 times, it gets a higher score than a plot that mentions it once).
2.  **Inverse Document Frequency (IDF)**: How rare is this search term across the *entire index* (all 30 movies)?
    *   *Rarer words = higher score.* Common words like "the" or "movie" have low IDF. A rare word like "sci" or "Alien" has a very high IDF, meaning matching it is worth many more points.
3.  **Field Length Normalization**: How long is the field where the match occurred?
    *   *Shorter fields = higher score.* A match in a short title is much more important than a match in a very long plot description, because in a short title, that word represents a larger percentage of the document's topic.

### B. Why did "Alien" score slightly higher than "Eternal Sunshine" for `q=sci`?
*   **Alien (Score: 2.767)**: Plot is 21 words: *"...Sci-fi horror classic."*
*   **Eternal Sunshine (Score: 2.651)**: Plot is 21 words: *"...Romantic sci-fi drama."*
*   Both plots have a similar length and contain the word "sci" once. However, OpenSearch scores depend on **shard-level statistics**. Minor scoring variations occur because of the position of the term in the token stream, local shard frequencies, or the presence of other common words that affect overall document length calculations.

### C. The Math of Boosting (`boost`)
A boost is a direct multiplier applied to the base BM25 score of a field before all field scores are summed.

$$\text{Final Score} = \sum \left( \text{BM25 Score of Field} \times \text{Field Boost Factor} \right)$$

In our C# service, we have:
*   `Title` Boost: **`2.0`**
*   `DirectorName` / `ActorNames` Boost: **`1.5`**
*   `Plot` Boost: **`1.0`** (default)

#### Example Scenario: User searches `q=Alien`
1.  **Movie 1: "Alien"**:
    *   Matches `"Alien"` in `Title`. Base BM25 score = `2.5`. Boosted score = `2.5 * 2.0 = 5.0`.
    *   Matches `"Alien"` in `Plot`. Base BM25 score = `1.2`. Boosted score = `1.2 * 1.0 = 1.2`.
    *   **Total Score** = `5.0 + 1.2 = 6.2`.
2.  **Movie 2: "Jurassic Park"**:
    *   Matches `"alien"` in `Plot` (*"...touting an almost complete theme park on an island..."* - let's assume it had the word "alien" in the plot). Base BM25 score = `1.2`. Boosted score = `1.2 * 1.0 = 1.2`.
    *   **Total Score** = `1.2`.

Because of the `Title` boost, Movie 1 scores **5 times higher** than Movie 2, pushing the exact title match to the top.

### D. Scores are Relative, Not Absolute
You cannot compare the score of `2.76` for `q=sci` with `7.70` for `q=Matrix`. They are calculated using different term frequencies and document frequencies. 

A score is only meaningful when comparing documents **within the exact same search query results list** to determine their order.

---

## 8. Parameterizing C# Search Queries: Real-world Examples

Here are concrete C# examples showing how to customize and parameterize `client.SearchAsync` in the NEST/OpenSearch.Client library:

### Example A: Strict Single-Field Search (No Fuzziness)
If you want to query specifically by title, and *only* return exact/stemmed matches (no typos allowed), you target the `Title` field directly and remove fuzziness:
```csharp
public async Task<ISearchResponse<MovieSearchDocument>> SearchByTitleOnlyAsync(string titleQuery)
{
    return await client.SearchAsync<MovieSearchDocument>(s => s
        .Index(IndexName)
        .Query(q => q
            .Match(m => m
                .Field(f => f.Title)
                .Query(titleQuery)
                .Fuzziness(Fuzziness.Zero) // Strict matching
            )
        )
    );
}
```

### Example B: Filtering by Year Ranges (`Range` Query)
To search for a term *and* restrict the results to movies released within a specific timeframe (e.g. 1990 to 2000), you combine a match query with a range query using a `bool` query:
```csharp
public async Task<ISearchResponse<MovieSearchDocument>> SearchWithYearRangeAsync(string queryText, int minYear, int maxYear)
{
    return await client.SearchAsync<MovieSearchDocument>(s => s
        .Index(IndexName)
        .Query(q => q
            .Bool(b => b
                .Must(mu => mu
                    .MultiMatch(mm => mm
                        .Fields(f => f.Fields(m => m.Title, m => m.Plot))
                        .Query(queryText)
                    )
                )
                .Filter(fi => fi
                    .Range(r => r
                        .Field(f => f.ReleaseYear)
                        .GreaterThanOrEquals(minYear)
                        .LessThanOrEquals(maxYear)
                    )
                )
            )
        )
    );
}
```

### Example C: Fully Dynamic / Parameterized Multi-Filter Search
In production, you often build a search method that accepts a model with optional filters. Here is how to build a query dynamically in C# based on what filters the user filled out:

```csharp
public class SearchFilters
{
    public string? Keyword { get; set; }
    public string? Genre { get; set; }
    public int? MinYear { get; set; }
    public int? MaxYear { get; set; }
    public double? MinRating { get; set; }
}

public async Task<ISearchResponse<MovieSearchDocument>> AdvancedSearchAsync(SearchFilters filters)
{
    return await client.SearchAsync<MovieSearchDocument>(s => s
        .Index(IndexName)
        .Query(q => q
            .Bool(b => b
                // 1. Text Search (Must)
                .Must(mu => {
                    if (!string.IsNullOrWhiteSpace(filters.Keyword))
                    {
                        return mu.MultiMatch(mm => mm
                            .Fields(f => f
                                .Field(m => m.Title, boost: 2.0)
                                .Field(m => m.Plot)
                            )
                            .Query(filters.Keyword)
                            .Fuzziness(Fuzziness.Auto)
                        );
                    }
                    return mu.MatchAll(); // If no text keyword, return all matching filter criteria
                })
                // 2. Strict Filters (Filter)
                .Filter(fi => {
                    var filterList = new List<QueryContainer>();

                    // Exact Genre filter
                    if (!string.IsNullOrWhiteSpace(filters.Genre))
                    {
                        filterList.Add(new TermQuery
                        {
                            Field = Infer.Field<MovieSearchDocument>(f => f.Genres),
                            Value = filters.Genre
                        });
                    }

                    // Release Year range filter
                    if (filters.MinYear.HasValue || filters.MaxYear.HasValue)
                    {
                        var rangeQuery = new NumericRangeQuery
                        {
                            Field = Infer.Field<MovieSearchDocument>(f => f.ReleaseYear),
                            GreaterThanOrEqualTo = filters.MinYear,
                            LessThanOrEqualTo = filters.MaxYear
                        };
                        filterList.Add(rangeQuery);
                    }

                    // Rating threshold filter
                    if (filters.MinRating.HasValue)
                    {
                        var rangeQuery = new NumericRangeQuery
                        {
                            Field = Infer.Field<MovieSearchDocument>(f => f.Rating),
                            GreaterThanOrEqualTo = filters.MinRating
                        };
                        filterList.Add(rangeQuery);
                    }

                    return filterList.ToArray();
                })
            )
        )
    );
}
```

---

## 9. Understanding Case Insensitivity: Analyzers vs. Fuzziness

When you search for `"Matrix"` or `"matrix"` or `"sci"`, the search matches successfully regardless of capital letters. 

### Why does case insensitivity work?
It works entirely because of the **Linguistic Analyzer (`english`)**, and specifically its **Lowercase Token Filter**. It has nothing to do with Fuzziness.

### How the Analyzer Processes Text
When a field is mapped as `text` with the `english` analyzer, it processes string inputs through three steps:
1.  **Tokenizer**: Splits sentences into words (e.g. `"The Matrix"` becomes `["The", "Matrix"]`).
2.  **Token Filters**:
    *   **Lowercase Filter**: Converts all tokens to lowercase (e.g. `["The", "Matrix"]` $\rightarrow$ `["the", "matrix"]`).
    *   **Stemming Filter**: Reduces words to root forms (e.g. `"hackers"` $\rightarrow$ `"hacker"`).
    *   **Stop-words Filter**: Removes words like `"the"`, `"and"`, `"of"`.

This analyzer runs at **two distinct moments**:

#### 1. At Indexing Time (Writing Data)
When the movie *The Matrix* is indexed, OpenSearch analyzes the title and stores only the lowercase token **`"matrix"`** in the Inverted Index database.

#### 2. At Search Time (Querying Data)
When you query `q=Matrix` (with a capital M), OpenSearch runs your query text through the **exact same `english` analyzer**. The query term is lowercased to `"matrix"`.

Because both the stored index terms and your query terms are lowercased, they match exactly:

$$\text{Query: } \text{"Matrix"} \xrightarrow{\text{Analyzer}} \text{\textbf{"matrix"}} \equiv \text{Index: } \text{\textbf{"matrix"}} \leftarrow \text{Analyzer} \xleftarrow{\text{Indexed: }} \text{"The Matrix"}$$

Even if you set **`Fuzziness(Fuzziness.Zero)`**, case-insensitive searches will still match perfectly.

---

### The `keyword` Exception: A Common Gotcha
If you search a **`keyword` field** (like our `genres` field) using a strict `Term` query:
*   `keyword` fields **bypass the analyzer**. They are stored exactly as written, retaining their original case (e.g. `"Sci-Fi"`).
*   If you query a `keyword` field looking for `"sci-fi"` (lowercase), the match will **fail** because it expects the exact capital letters `"Sci-Fi"`.
*   This is why filters in UIs are usually built using exact dropdown values, rather than free-text inputs.

---

## 10. Advanced EF Core Concepts

### A. Eager Loading (Include & ThenInclude)
By default, EF Core does not load related tables to keep queries lightweight (called **Lazy Loading** or **Deferred Loading**). If you want related tables loaded in a single query, you must explicitly ask for them using `Include` and `ThenInclude`.

Example (from our sync route):
```csharp
var movies = await dbContext.Movies
    .Include(m => m.Director)                              // Eager load 1-to-many relationship
    .Include(m => m.Genres)                                // Eager load many-to-many relationship
    .Include(m => m.MovieActors)                           // Eager load join table MovieActor
        .ThenInclude(ma => ma.Actor)                       // Eager load related Actor inside the join table
    .ToListAsync();
```

---

### B. The N+1 Query Problem
The **N+1 Problem** occurs when an application executes `1` query to fetch a list of parent rows, and then executes `N` separate database queries (one for each row) to fetch their child relationships.

#### Example of the N+1 Trap:
```csharp
// 1. Executes 1 Query to get all movies (N = 30 movies returned)
var movies = await dbContext.Movies.ToListAsync();

// 2. Loop through each movie to get its director (triggers 30 separate SQL queries!)
foreach (var movie in movies)
{
    var directorName = movie.Director.Name; // Triggers: SELECT * FROM Directors WHERE Id = @Id
}
```
*   **Result**: 31 database queries executed! (1 query for movies + 30 queries for directors). This degrades database performance.
*   **Prevention**: By using **Eager Loading** (`.Include(m => m.Director)`), EF Core generates a single SQL query with an `INNER JOIN` or `LEFT JOIN`, returning all data in **1 single database roundtrip**.

---

### C. No-Tracking Queries (`AsNoTracking`)
When you query entities, EF Core places a copy of them in its internal **Change Tracker**. This tracker monitors any updates you make to the objects so it knows what SQL `UPDATE` queries to run when you call `SaveChangesAsync()`.

If you are running a **read-only** query (like syncing to OpenSearch or displaying a catalog list), tracking is an unnecessary overhead. You disable it using `.AsNoTracking()`:
```csharp
var movies = await dbContext.Movies
    .AsNoTracking()
    .Include(m => m.Director)
    .ToListAsync();
```
*   **Pros**: Significant performance boost. Uses less memory and runs query mappings faster because EF Core bypasses building tracking graphs.

---

### D. DbContext Pooling & Connection Factories
In a web application, creating and tearing down database connections for every request is expensive and can exhaust the host's ports (socket exhaustion).

*   **Standard Lifecycle**: Every HTTP request gets a new `AppDbContext` instance (and a new database connection), which is closed on request completion.
*   **DbContext Pooling**: Instead of destroying the context, ASP.NET Core places the context instance back into a **pool** when the HTTP request finishes. The next incoming HTTP request reuses the pre-created context.
*   We register this in `Program.cs` in production using:
    ```csharp
    builder.Services.AddDbContextPool<AppDbContext>(options => ...);
    ```
    This significantly increases throughput by cutting context instantiation and database connection handshake overhead.









