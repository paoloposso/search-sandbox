---
name: opensearch-integration
description: Guidelines and C# code templates for mapping, indexing, and querying text vs keyword fields in OpenSearch.
---
# OpenSearch Integration Skill

This skill documents how to structure and query OpenSearch indexes based on field types.

## 1. Text vs. Keyword properties

| Aspect | `text` field type | `keyword` field type |
| :--- | :--- | :--- |
| **Use Case** | Search bars, full plots, body texts. | Categories, IDs, status tags, exact genres. |
| **Analysis** | Yes (analyzed, stemmed, lowercased). | No (stored exactly as-is on disk). |
| **Case Sensitive** | No (due to lowercase filter). | Yes (exact capital match required). |
| **Queries** | `Match`, `MultiMatch` with Fuzziness. | `Term` (single value), `Terms` (list value). |

---

## 2. C# Mapping Configurations

### Defining properties in C# Index setup (from `OpenSearchService.cs`):
```csharp
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
```

---

## 3. C# Querying Templates

### A. MultiMatch Query with Field Boosting and Fuzziness:
```csharp
q.MultiMatch(mm => mm
    .Fields(f => f
        .Field(movie => movie.Title, boost: 2.0)
        .Field(movie => movie.Plot)
        .Field(movie => movie.DirectorName, boost: 1.5)
        .Field(movie => movie.ActorNames, boost: 1.5)
        .Field(movie => movie.Genres, boost: 1.5)
    )
    .Query(queryText)
    .Fuzziness(Fuzziness.Auto)
)
```

### B. SimpleQueryString Query:
```csharp
q.SimpleQueryString(sqs => sqs
    .Fields(f => f
        .Field(movie => movie.Title, boost: 2.0)
        .Field(movie => movie.Plot)
        .Field(movie => movie.DirectorName, boost: 1.5)
        .Field(movie => movie.ActorNames, boost: 1.5)
        .Field(movie => movie.Genres, boost: 1.5)
    )
    .Query(queryText)
)
```

### C. Filtering by Keyword (Genre Match):
```csharp
q.Term(t => t.Field(m => m.Genres).Value(genre))
```

