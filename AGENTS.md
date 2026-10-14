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

## Agent skills

### Issue tracker

Issues are tracked as GitHub issues in this repo. See `docs/agents/issue-tracker.md`.

### Triage labels

The label vocabulary uses the five canonical roles with default names. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout. See `docs/agents/domain.md`.

<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:7510c1e2 -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

**Architecture in one line:** issues live in a local Dolt DB; sync uses `refs/dolt/data` on your git remote; `.beads/issues.jsonl` is a passive export. See https://github.com/gastownhall/beads/blob/main/docs/SYNC_CONCEPTS.md for details and anti-patterns.

## Session Completion

**When ending a work session**, you MUST complete ALL steps below. Work is NOT complete until `git push` succeeds.

**MANDATORY WORKFLOW:**

1. **File issues for remaining work** - Create issues for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **PUSH TO REMOTE** - This is MANDATORY:
   ```bash
   git pull --rebase
   git push
   git status  # MUST show "up to date with origin"
   ```
5. **Clean up** - Clear stashes, prune remote branches
6. **Verify** - All changes committed AND pushed
7. **Hand off** - Provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds
<!-- END BEADS INTEGRATION -->
