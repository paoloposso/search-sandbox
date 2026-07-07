---
name: go-vespa-feeder
description: Instructions and templates for writing the high-performance concurrent Go streamer to push Postgres records into Vespa document endpoints.
---
# Go Vespa Feeder Skill

Guidelines for building the concurrent data feeder in Go.

## Requirements
- Source database: PostgreSQL.
- Target service: Vespa `/document/v1/` REST API.
- Use concurrent Go channels and Goroutines to stream records.

## Vespa Feed Endpoint Format
Send POST requests to:
`http://localhost:8080/document/v1/movie/movie/docid/<movie_id>`

Payload structure:
```json
{
  "fields": {
    "title": "Movie Title",
    "plot": "Movie Plot...",
    "release_year": 1999,
    "rating": 8.7,
    "genres": ["Action", "Sci-Fi"]
  }
}
```
Ensure HTTP client configurations reuse connections (Keep-Alive) and control concurrency to avoid overwhelming the Vespa container.
