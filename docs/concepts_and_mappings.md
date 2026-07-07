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

