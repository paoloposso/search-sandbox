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
