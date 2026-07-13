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

## Deploying Configuration Locally
To package and deploy the local `/vespa` configuration package to the running Docker container config server in a single command, run from the project root:
```bash
cd vespa && zip -r ../application.zip services.xml schemas/ && cd .. && curl --header "Content-Type: application/zip" --data-binary @application.zip http://localhost:19071/application/v2/tenant/default/prepareandactivate && rm application.zip
```

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

### Testing Vespa YQL Queries via CLI
You can execute and inspect YQL queries directly inside the running Docker container using the pre-installed Vespa CLI:

```bash
# General unstructured text search with custom ranking
docker exec movies-vespa vespa query "query=interstellar" "ranking=movie_rank"

# Structured numeric filters (e.g. ratings > 9.0)
docker exec movies-vespa vespa query 'select * from movie where rating > 9.0'

# Array filters (e.g. match list values)
docker exec movies-vespa vespa query 'select * from movie where genres contains "Sci-Fi"'

# Logical combinations (e.g. 90s Sci-Fi classics)
docker exec movies-vespa vespa query 'select * from movie where genres contains "Sci-Fi" and release_year >= 1990 and release_year <= 1999'

# Sorting and limits
docker exec movies-vespa vespa query 'select * from movie where true order by rating desc limit 5'

# Multi-Match Search (Combine text search with structural filters)
docker exec movies-vespa vespa query "yql=select * from movie where userQuery() and genres contains 'Sci-Fi'" "query=hacker"
```
