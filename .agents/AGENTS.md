# Project Rules & Design Guidelines

This workspace implements a **Dual-Search Evaluation Architecture** comparing OpenSearch and Vespa.ai side-by-side.

## Technical Frameworks
- **Primary API**: C# (.NET 10.0 Web API using Minimal APIs).
- **Primary Database**: PostgreSQL 16.
- **Search Services**: OpenSearch 2.12 (standard keyword indexing) and Vespa.ai (ranking-profile tensor search).
- **Ingestion Streamer**: Go (concurrent feeding).

## Architectural Guidelines
1. **Source of Truth**: PostgreSQL is the transactional system of record. Do not update indexes directly; updates must go through Postgres and be replicated/streamed.
2. **Read-Only Search Views**: OpenSearch and Vespa.ai are read-only views optimized for search queries.
3. **Relevance Benchmarking**: Any unified search queries must capture and expose latency metric timers and matching scores from both platforms.

## C# Collection Design: IEnumerable vs List

### 1. Guideline
*   **Parameters & Return Types**: Prefer `IEnumerable<T>` for parameters to make methods highly generic and decoupled from implementation.
*   **Data Transport / DTOs**: Use `IEnumerable<T>` for child lists (like `Genres` inside `MovieSearchDocument`) to avoid allocating unnecessary `List<T>` objects during serialization.
*   **Database Queries**: Use `IQueryable<T>` for composing DB queries, and convert to `List<T>` via `.ToListAsync()` once the data needs to be evaluated into memory.

### 2. The Multiple Enumeration Gotcha
Because `IEnumerable<T>` uses **lazy evaluation**, iterating over it twice (e.g., calling `.Count()` and then iterating over it to index) causes the collection logic or DB projection mapping to execute the items twice.
*   **Bad**:
    ```csharp
    var docs = movies.Select(x => MapToDoc(x));
    var count = docs.Count(); // First enumeration (runs loop to count)
    await Sync(docs);         // Second enumeration (runs loop to serialize)
    ```
*   **Good (Use materialized list count)**:
    ```csharp
    var docs = movies.Select(x => MapToDoc(x));
    var count = movies.Count; // Read count directly from the materialized database list (no loop)
    await Sync(docs);         // Single enumeration occurs during serialization
    ```

