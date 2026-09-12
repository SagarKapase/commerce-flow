# CommerceFlow — Working Agreement

This file governs how the assistant works in this repository. It applies to every session.
The full plan lives in [docs/PRD.md](docs/PRD.md).

## Non-negotiable rules

1. **One phase at a time. One microservice at a time.** Never build ahead.
2. **Never create placeholder projects** for future services. The solution grows visibly.
3. **Controllers for every HTTP endpoint.** No Minimal APIs for business functionality.
4. **No magic.** Show explicit configuration in `Program.cs` first; refactor into extension
   methods only later, as a visible and explained refactor.
5. **Justify every abstraction** with four questions: what problem exists now, what does the
   pattern solve, why now, what simpler alternative was rejected. If it isn't needed, don't add it.
6. **Banned unless explicitly requested:** Docker, Kubernetes, cloud, CI/CD, React/Angular,
   Redis, Kafka, Elasticsearch, MediatR, AutoMapper, generic repositories, `Repository<T>`,
   `UnitOfWork<T>`, `BaseService<T>`.
7. **Comments explain WHY**, never what.
8. **Every phase ends with a STOP**: acceptance criteria, a debug checklist with breakpoint
   locations and a watch list, interview questions — then stop and wait for
   "Phase X working" before continuing.
9. **Error mode.** Given a compiler error, runtime error, exception or migration failure:
   stop adding features and answer as **Problem → Likely Cause → Fix (smallest change) →
   Why It Happened → Verify**.
10. **Never assume it works.** Verification always includes inspecting the database rows.

## Phase flow

Explain the goal → show the structure → create files in small groups with explanations →
migration + conceptual SQL → manual Swagger test guide → debug checklist → interview questions → STOP.

## Conventions

- Databases: SQLite, one file per service, never shared.
- Ports: HTTP `51xx`, HTTPS `71xx` (see PRD Appendix A).
- Money: `decimal` in the domain, stored as integer minor units via an EF value converter.
- Commits: Conventional Commits, one per working step, never one giant commit per phase.
