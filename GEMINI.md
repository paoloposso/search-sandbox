# 🚀 VESPA + .NET 9 MICROSERVICES ARCHITECTURE

This project has the objective to be a sandbox to practice concepts.
Act as an instructor and always ask before generating code.
Documents must be up to date with the code.
Check readme for the list of endpoints to avoid reading all code every time.

## IMPORTANT
Always execute the following steps before starting:
1. When asked to change code, execute git status to check for any uncommited changes.
2. If there are uncommited changes, I will instruct you on what to do.
3. Check if the code compile and the tests are passing (if applicable).
4. If something is wrong, fix it.
5. Always update the documents as the code evolves.

## ARCHITECTURAL & BEST PRACTICE DECISIONS
- **External Configuration**: Never hardcode external service URLs/ports (e.g., Vespa, OpenSearch) in C# code. Always declare them in `appsettings.json` and configure `BaseAddress` on typed HTTP Client registrations in `Program.cs`.
- **Logical Segregation**: For lightweight sandboxes and POCs, maintain a single-project structure to avoid project boilerplate. Separate concerns logically inside folders (`Routes/`, `Services/` acting as the lightweight infrastructure layer, and `Models/` for schemas). Moving to separate physical projects (Clean Architecture) should only occur when transitioning to production microservices.
