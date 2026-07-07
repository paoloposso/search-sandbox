---
name: vespa-integration
description: Guidelines and configuration schemas for Vespa.ai application packages, YQL searches, and schema deployments.
---
# Vespa Integration Skill

This skill assists in configuring and deploying Vespa.ai application packages.

## Directory Structure
Vespa configurations are housed in `/vespa/`:
- `/vespa/services.xml`: Defines container and content cluster configuration.
- `/vespa/schemas/movie.sd`: Defines the document field mapping, index behaviors, and ranking profiles.

## Schema Guidelines
- Document names must match the search schema file name (e.g. `document movie` inside `movie.sd`).
- Explicitly set field types (e.g., `string`, `int`, `array<string>`).
- Implement custom ranking profiles:
  ```vespa
  rank-profile custom_rank inherits default {
      first-phase {
          expression: attribute(rating) * 0.3 + bm25(title) * 0.7
      }
  }
  ```

## Querying with YQL
Always construct structured YQL queries targeting the container endpoint:
- Path: `http://localhost:8080/search/`
- Parameter: `yql=select * from movie where userQuery()`
- Parameter: `query=search term`
