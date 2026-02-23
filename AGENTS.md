# AGENTS.md

Repository: Enphase (EnphaseLocal API + Tests)

Build / Run:
- Restore & build: `dotnet build Enphase.sln`
- Run API (Dev): `dotnet run --project EnphaseLocal`
- Run with explicit env: `dotnet run --project EnphaseLocal --environment Production`
- Docker (local): `docker build -t enphase-local .` then `docker run -p 8080:80 enphase-local`

Test:
- All tests: `dotnet test Tests/EnphaseLocal.Tests/EnphaseLocal.Tests.csproj`
- Single test by filter: `dotnet test Tests/EnphaseLocal.Tests/EnphaseLocal.Tests.csproj --filter FullyQualifiedName~EnphaseLocalTests.GetNetPowerProductionAsync_ReturnsExpectedValue`
- Collect coverage: `dotnet test --collect:"XPlat Code Coverage"`

Style & Conventions:
- Target Framework: net9.0, nullable + implicit usings enabled; do not disable.
- File-scoped namespaces; `using` directives at top (implicit usings allowed, avoid redundant imports).
- Prefer `async`/`await`; return concrete types (`Task<T>`, not `ValueTask` unless performance-proven).
- Use DI for HttpClient; no `new HttpClient()` outside tests; configure headers via delegating handlers/options.
- Exceptions: throw specific (`InvalidOperationException`, `HttpRequestException`); log with structured messages before rethrow.
- Logging: use injected `ILogger<T>`; Information for normal ops, Debug for verbose, Error before throwing.
- Naming: PascalCase for public types/members; private fields `_camelCase`; method parameters `camelCase`. Avoid abbreviations unless domain (e.g. DTO, HTTP).
- JSON: System.Text.Json; enable case-insensitive property names; avoid mixing Newtonsoft unless needed for features unavailable.
- Tests: xUnit; one Assert per behavior block when possible; use `Assert.ThrowsAsync<T>` for async error cases; prefer Moq & RichardSzalay.MockHttp for HTTP.
- Do not expose internals just for tests; use reflection sparingly (consider refactor if needed).
- Swagger: always enabled; endpoints `/swagger` and `/swagger/v1/swagger.json` should remain accessible in all environments.

No existing Cursor or Copilot rules detected; follow this file for agent guidance.
