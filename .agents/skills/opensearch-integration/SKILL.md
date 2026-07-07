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

## 2. C# Mapping Mappings

### Defining properties in C# Index setup:
```csharp
.Map<MovieSearchDocument>(m => m
    .Properties(p => p
        // Analyzed Text field (for titles, plots)
        .Text(t => t
            .Name(f => f.Title)
            .Analyzer("english")
        )
        // Strict Keyword field (for exact filtering)
        .Keyword(k => k
            .Name(f => f.Genres)
        )
    )
)
```

---

## 3. C# Querying Templates

### Querying Analyzed Text:
Use `Match` or `MultiMatch` queries (supports case-insensitivity, stemming, and fuzziness):
```csharp
q => q.Match(m => m
    .Field(f => f.Title)
    .Query("matrix")
    .Fuzziness(Fuzziness.Auto)
)
```

### Querying Keywords (Strict Filters):
Use `Term` (or `Terms` for arrays) queries (must match exact characters and casing):
```csharp
// Single value exact match
q => q.Term(t => t
    .Field(f => f.Genres)
    .Value("Sci-Fi") // Matches "Sci-Fi" exactly. "sci-fi" will fail.
)

// Array values exact match (OR check)
q => q.Terms(t => t
    .Field(f => f.Genres)
    .Terms(new[] { "Action", "Horror" })
)
```
