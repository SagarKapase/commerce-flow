# CommerceFlow — Product Requirements Document & Implementation Roadmap

**Version:** 1.0
**Date:** 2026-09-07
**Owner:** Uday (≈4 years .NET experience)
**Status:** Approved for Phase 0 (awaiting explicit "Start Phase 0")

> This document is the contract for how CommerceFlow gets built.
> It is written for a **strong mid-level .NET developer** who wants to be able to **explain every file in an interview**.
> No application code exists yet. Code begins only when the developer says "Start Phase 0".

---

## Table of Contents

| # | Section |
|---|---|
| 1 | Project Overview |
| 2 | Interview Goals |
| 3 | Functional Scope |
| 4 | Technical Scope |
| 5 | Explicitly Excluded Technologies |
| 6 | Microservice Architecture |
| 7 | Service Responsibilities |
| 8 | Database-per-Service Design |
| 9 | SQLite Strategy |
| 10 | Solution Explorer Structure |
| 11 | Dependency Rules |
| 12 | Coding Standards |
| 13 | API Design Guidelines |
| 14 | Error Handling Strategy |
| 15 | Validation Strategy |
| 16 | Logging Strategy |
| 17 | Debugging Strategy |
| 18 | Testing Strategy |
| 19 | Service Communication Roadmap |
| 20 | Messaging Roadmap |
| 21 | Saga Roadmap |
| 22 | Outbox Roadmap |
| 23 | Idempotency Roadmap |
| 24 | Security Roadmap |
| 25 | Phase-by-Phase Development Plan |
| 26 | Acceptance Criteria per Phase |
| 27 | Debug Checklist per Phase |
| 28 | Interview Questions per Phase |
| 29 | Git Commit Strategy |
| 30 | Definition of Done |
| A | Appendix A — Port Table |
| B | Appendix B — Working Agreement (how Claude must behave) |
| C | Appendix C — Key Architectural Decisions (ADR log) |

---

## 1. Project Overview

**CommerceFlow** is a distributed e-commerce backend built with ASP.NET Core microservices.

It exists for one reason: to be a **portfolio-grade, interview-defensible system** that its author fully understands — not a code dump.

Three things define the project:

1. **Incremental construction.** One microservice at a time. Run it, debug it, test it, understand it, then move on.
2. **Readability over cleverness.** If a line of code cannot be explained in an interview, it does not belong in the repo.
3. **Realistic architecture, minimal infrastructure.** Real microservice boundaries, real database-per-service, real eventual consistency — but running entirely on a local Windows machine with SQLite files and `F5`.

There is **no frontend**. Swagger UI is the interface.

### What "done" looks like at the end of the roadmap

A solution of 8 projects/services where a user can register, log in, browse a catalog, fill a basket, place an order, have inventory reserved, have a (simulated) payment processed, receive a notification, and see the order confirmed — with a saga that compensates correctly when payment fails, an outbox that guarantees events are published, and idempotency that survives duplicate delivery.

---

## 2. Interview Goals

Every phase must produce **explainable knowledge**, not just working code. By the end you must be able to answer, from your own code:

**Architecture**
- Why microservices instead of a modular monolith? What did we actually gain and lose?
- Why is Catalog separate from Inventory when both talk about products?
- What does "database per service" mean, and what breaks if you violate it?
- Where is the boundary of a transaction in a distributed system?
- Why did the order flow move from synchronous HTTP to messaging?

**ASP.NET Core**
- Request pipeline: routing → model binding → validation → controller → application → EF Core → SQLite → response.
- `[ApiController]` behaviours, `ActionResult<T>` vs `IActionResult`, `CreatedAtAction`, ProblemDetails.
- DI lifetimes (`Scoped`, `Singleton`, `Transient`) and why `DbContext` is scoped.
- Middleware ordering, authentication vs authorization, JWT validation.

**EF Core**
- Change tracking, `AsNoTracking`, `SaveChangesAsync`, migrations, value converters, optimistic concurrency.
- What SQL EF Core generated, and why.

**Distributed systems**
- Eventual consistency, saga + compensation, outbox, idempotency, at-least-once delivery.

**Engineering**
- How you debugged it. Where you put breakpoints. What you inspected.

> **Rule:** at the end of each phase you will be asked interview questions about the code you just wrote. If you cannot answer them, we revisit the code before moving on.

---

## 3. Functional Scope

### 3.1 Customer capabilities (target end state)

| Capability | Owning service | Introduced in |
|---|---|---|
| Register an account | Identity | Phase 2 |
| Log in, refresh token, log out | Identity | Phase 2 |
| Browse products (list, filter, search, paginate) | Catalog | Phase 1 |
| View product details | Catalog | Phase 1 |
| Browse categories | Catalog | Phase 1 |
| Add item to basket | Basket | Phase 4 |
| Update basket item quantity | Basket | Phase 4 |
| Remove item / clear basket | Basket | Phase 4 |
| Basket shows live product name + price | Basket → Catalog | Phase 5 |
| Place an order | Ordering | Phase 7–8 |
| Inventory reserved for the order | Inventory | Phase 6–8 |
| Payment simulated | Payment | Phase 9 |
| View order status | Ordering | Phase 7 |
| View order history | Ordering | Phase 7 |
| Cancel an eligible order | Ordering | Phase 7 |
| Receive an "order placed" notification | Notification | Phase 10 |

### 3.2 Administrator capabilities

| Capability | Owning service | Introduced in |
|---|---|---|
| Create / update product | Catalog | Phase 1 (open), Phase 3 (secured) |
| Deactivate product (soft delete) | Catalog | Phase 1 |
| Create / update category | Catalog | Phase 1 |
| Adjust stock | Inventory | Phase 6 |
| View all orders | Ordering | Phase 7 |

### 3.3 Explicitly out of scope (forever, for this project)

Shipping/logistics, tax engines, coupons/promotions, product reviews, recommendations, multi-tenancy, multi-currency, real payment providers, real email/SMS delivery, a UI.

We may *mention* these as "how it would extend"; we will not build them.

---

## 4. Technical Scope

| Concern | Choice | Notes |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | `net10.0` target framework everywhere |
| API style | **ASP.NET Core Controllers** | Minimal APIs explicitly rejected — see §13 |
| ORM | **EF Core 10** | Code-first + migrations |
| Database | **SQLite**, one file per service | Replaceable later — see §9 |
| API docs | **Swashbuckle (Swagger UI)** | Classic Swagger UI + `Authorize` button for JWT |
| Auth | **ASP.NET Core Identity + JWT Bearer** | Phase 2 |
| Logging | **`ILogger<T>`** → Serilog later | Phase 1 → Phase 17 |
| Validation | **DataAnnotations** → FluentValidation only if justified | §15 |
| Errors | **ProblemDetails + `IExceptionHandler`** | §14 |
| HTTP calls | **Typed `HttpClient` via `IHttpClientFactory`** | Phase 5 |
| Messaging | **RabbitMQ** (native Windows install) | Phase 10, decision point documented |
| Gateway | **YARP** | Phase 15 |
| Tests | **xUnit + Shouldly + `Microsoft.AspNetCore.Mvc.Testing`** | Phase 16 |
| IDE | Visual Studio 2026 / VS Code + `dotnet` CLI | F5 debugging is a first-class requirement |

**Note on FluentAssertions:** we use **Shouldly** instead, because FluentAssertions v8+ requires a paid licence for commercial use. Same readability, no licensing footgun. Worth knowing in an interview.

---

## 5. Explicitly Excluded Technologies

These are **banned until explicitly requested by the developer**:

- React, Angular, any frontend UI
- Docker, Docker Compose, Kubernetes
- Azure, AWS, any cloud service
- CI/CD pipelines
- Kafka, Elasticsearch
- Redis (Basket uses SQLite instead — migration path documented in §7.3)
- Service meshes, service discovery systems (Consul, Eureka, K8s DNS)
- gRPC (HTTP/JSON first; gRPC is a possible later exercise)
- MediatR, AutoMapper, CQRS frameworks (see §12.4 — each must be *earned*)
- Generic repository / Unit of Work base classes

**One flagged exception to discuss later:** RabbitMQ (Phase 10) normally arrives via Docker. Since Docker is banned, Phase 10 opens with a decision: install RabbitMQ natively on Windows (Erlang + RabbitMQ Server as a Windows service, management UI on `localhost:15672`), or lift the Docker ban for that one component. **This decision is deferred to Phase 10 and is the developer's to make.** Nothing before Phase 10 depends on it.

---

## 6. Microservice Architecture

### 6.1 Final target topology

```text
                          ┌──────────────────────┐
                          │  API Gateway (YARP)  │  :5100
                          │   Phase 15           │
                          └──────────┬───────────┘
                                     │  HTTP (path-based routing)
      ┌───────────────┬──────────────┼──────────────┬───────────────┐
      │               │              │              │               │
┌─────▼─────┐   ┌─────▼─────┐  ┌─────▼─────┐  ┌─────▼─────┐  ┌──────▼─────┐
│  Catalog  │   │ Identity  │  │  Basket   │  │ Inventory │  │  Ordering  │
│   :5101   │   │   :5102   │  │   :5103   │  │   :5104   │  │   :5105    │
│catalog.db │   │identity.db│  │basket.db  │  │inventory.db│ │ordering.db │
└───────────┘   └───────────┘  └─────┬─────┘  └─────▲─────┘  └──────┬─────┘
      ▲                               │  HTTP        │  HTTP         │
      └───────────────────────────────┘              └───────────────┘
                                                                     │
                                       ┌─────────────────────────────┴────┐
                                       │        RabbitMQ (Phase 10)       │
                                       └───┬──────────────────────────┬───┘
                                           │                          │
                                   ┌───────▼──────┐          ┌────────▼────────┐
                                   │   Payment    │          │  Notification   │
                                   │    :5106     │          │    .Worker      │
                                   │  payment.db  │          │  (no HTTP port) │
                                   └──────────────┘          └─────────────────┘
```

### 6.2 Communication rules

| Rule | Reason |
|---|---|
| A service **never** reads another service's database | The whole point of database-per-service |
| Cross-service references are **plain IDs, no foreign keys** | Databases are physically separate |
| Synchronous HTTP only for **queries that must be fresh** and for **commands the caller must confirm now** | Keeps latency and coupling visible |
| Asynchronous events for **anything that can be eventually consistent** | Decoupling, resilience |
| Each service owns its DTO contracts; **no shared entity library** | Shared models re-couple services at compile time |
| The only shared code is **cross-cutting technical plumbing** (messaging plumbing, error middleware) | See §10.4 BuildingBlocks rule |

### 6.3 Why microservices here at all (the honest answer)

For a shop of this size, a modular monolith would be the correct production choice. We build microservices because the *learning objective* is distributed systems: boundaries, eventual consistency, sagas, outbox, idempotency. **Say exactly this in an interview** — it demonstrates judgement, which is more valuable than enthusiasm.

---

## 7. Service Responsibilities

### 7.1 Catalog Service — *"What can be bought"*

- Owns: `Product`, `Category`.
- Owns: name, description, SKU, price, category, active flag.
- Does **not** own: stock levels (Inventory), basket contents, orders.
- Endpoints: `ProductsController`, `CategoriesController`.
- Read-heavy. No auth in Phase 1; admin-only writes from Phase 3.

### 7.2 Identity Service — *"Who is asking"*

- Owns: users, password hashes, roles, refresh tokens.
- Issues JWTs; every other service **validates** tokens locally without calling Identity. This is the single most important auth lesson in the project.
- Endpoints: `AuthController`, `UsersController`.
- Project layout: `Identity.Api`, `Identity.Application`, `Identity.Infrastructure` — **no Domain project** (deviation explained in §10.3).

### 7.3 Basket Service — *"What a user intends to buy"*

- Owns: one basket per user, its items and quantities.
- Basket owner comes from the **JWT `sub` claim**, never from the URL or body.
- Stores a **price snapshot** taken at add-time; refreshes display data from Catalog (Phase 5).
- Project layout: **`Basket.Api` only** (deviation explained in §10.3).
- **Redis migration path (not now):** the basket is a key-value document keyed by user id. Swapping SQLite for Redis means replacing one persistence class (`IBasketRepository` implementation) and its serialization; no controller, DTO, or business change. We stay on SQLite so the data is inspectable with a DB browser and requires no extra install.

### 7.4 Inventory Service — *"What can actually be shipped"*

- Owns: `InventoryItem` (available/reserved quantities per product), `InventoryReservation`.
- Business rules: cannot reserve more than available; reservations are released or confirmed; adjustments are audited.
- Teaches **optimistic concurrency** — two orders racing for the last unit.
- Endpoints: `InventoryController`, `InventoryReservationsController`.

### 7.5 Ordering Service — *"The commitment"*

- Owns: `Order`, `OrderItem`, order state machine.
- The **domain-heaviest** service; the place where a real Domain project earns its keep.
- Order items are **snapshots** (product id, name, unit price at order time). Catalog price changes must never rewrite order history.
- Endpoints: `OrdersController` — business intent operations, not CRUD.

### 7.6 Payment Service — *"Did the money move"*

- Owns: `Payment` records and their outcome.
- **Simulated** payments driven by explicit test tokens (see §25 Phase 9).
- Project layout: **`Payment.Api` only** initially (deviation explained in §10.3).

### 7.7 Notification Service — *"Tell the human"*

- Pure message consumer. **No controller, no HTTP port.**
- Writes structured log lines instead of sending email.
- Exists mainly to teach: *not every microservice is a Web API*.

### 7.8 API Gateway

- Single entry point, path-based routing (YARP), optional JWT validation at the edge.
- **Zero business logic. Zero controllers.**

---

## 8. Database-per-Service Design

### 8.1 The rule

Every service owns exactly one database file. No service opens another service's file. No cross-database joins. No shared `DbContext`.

```text
src/Services/Catalog/Catalog.Api/catalog.db
src/Services/Identity/Identity.Api/identity.db
src/Services/Basket/Basket.Api/basket.db
src/Services/Inventory/Inventory.Api/inventory.db
src/Services/Ordering/Ordering.Api/ordering.db
src/Services/Payment/Payment.Api/payment.db
```

### 8.2 What this forces you to learn

| Monolith habit | Microservice reality | Where you'll feel it |
|---|---|---|
| `JOIN Products ON OrderItems.ProductId` | Order stores a *copy* of product name and price | Phase 7 |
| FK constraint guarantees the product exists | Ordering must *ask* Catalog, or trust the basket | Phase 8 |
| One transaction covers order + stock | Two databases, two transactions, one saga | Phase 11 |
| `SELECT` across everything for a report | Data must be composed at the API layer or projected via events | Phase 8 |

### 8.3 Cross-service identifiers

`Ordering.OrderItem.ProductId` is a `Guid` column with **no foreign key**. It is a reference to data owned elsewhere. This looks "wrong" to a monolith developer and is exactly right here — expect to be asked about it.

---

## 9. SQLite Strategy

### 9.1 Why SQLite for this project

- **Zero install, zero service, zero Docker.** `F5` works on a clean machine.
- **Inspectable.** Open the `.db` file in DB Browser for SQLite and see rows immediately — critical for the "verify the row exists" debugging habit.
- **Disposable.** Delete the file, re-run migrations, start clean.
- It keeps the focus on **service boundaries**, not database administration.

### 9.2 Why SQLite is not the production answer

- Single-writer locking; no real concurrent write throughput.
- No native `decimal`, no `rowversion`, weak type affinity.
- No server-side users/roles/network access.
- Limited `ALTER TABLE` (EF works around it by rebuilding tables — visible in migrations).

### 9.3 Migration path (say this in interviews)

Because each service owns its own `DbContext` and connection string, switching a service to PostgreSQL or SQL Server means:

1. Swap the provider package and `UseSqlite(...)` → `UseNpgsql(...)`.
2. Change the connection string in `appsettings.json`.
3. Delete and regenerate that service's migrations (providers generate different SQL).
4. Revisit provider-specific mappings (money, concurrency tokens, `DateTimeOffset`).

**Service boundaries, contracts, controllers, DTOs and domain logic do not change.** That is the payoff of the layering.

### 9.4 Two SQLite gotchas we will handle deliberately

**(a) Money.** EF Core's SQLite provider stores `decimal` as `TEXT` and warns that comparison/ordering is unsupported — meaning `WHERE Price >= 1000` would compare strings lexicographically and give wrong results for our price-range filter. **Decision:** the domain uses `decimal`, and EF Core persists it via a value converter to `long` **minor units** (paise/cents) stored as `INTEGER`. Exact arithmetic, correct sorting and range filters, one small conversion documented in the entity configuration. (Alternative rejected: `HasConversion<double>()` — floating-point money is a bad habit.)

**(b) Concurrency tokens.** SQLite has no `rowversion`. **Decision:** Inventory uses an `int Version` column configured as a concurrency token and incremented explicitly on each change. Same optimistic-concurrency behaviour, provider-agnostic. Covered in Phase 6.

### 9.5 Where the `.db` file lands, and why

Connection string: `"Data Source=catalog.db"` — a *relative* path. SQLite resolves it against the **process working directory**, which for both `dotnet run` and a default Visual Studio launch profile is the **API project folder** (not `bin\Debug\`). So the file appears next to `Catalog.Api.csproj`. We will verify this in the Phase 1 debug checklist rather than assume it. All `*.db`, `*.db-shm`, `*.db-wal` files are git-ignored.

### 9.6 Migrations policy

- Migrations live in the **Infrastructure** project (they are persistence concerns); the **Api** project is the startup project (it has the DI/configuration wiring EF's design-time tooling needs).
- We run `dotnet ef database update` **manually** during learning, so migrations are a visible, deliberate step.
- We do **not** call `Database.Migrate()` on startup. (Trade-off explained in Phase 1: convenient for demos, dangerous with multiple instances and irreversible schema changes.)
- We never call `EnsureCreated()` — it bypasses migrations entirely.

---

## 10. Solution Explorer Structure

### 10.1 Final target (built up over 18 phases — not created up front)

```text
CommerceFlow.sln
.editorconfig
.gitignore
Directory.Build.props
README.md
docs/
└── PRD.md

src/
├── Gateway/
│   └── CommerceFlow.ApiGateway/          (Phase 15)
│
├── Services/
│   ├── Catalog/                          (Phase 1)
│   │   ├── Catalog.Domain/
│   │   ├── Catalog.Application/
│   │   ├── Catalog.Infrastructure/
│   │   └── Catalog.Api/
│   │
│   ├── Identity/                         (Phase 2)
│   │   ├── Identity.Application/
│   │   ├── Identity.Infrastructure/
│   │   └── Identity.Api/
│   │
│   ├── Basket/                           (Phase 4)
│   │   └── Basket.Api/
│   │
│   ├── Inventory/                        (Phase 6)
│   │   ├── Inventory.Domain/
│   │   ├── Inventory.Application/
│   │   ├── Inventory.Infrastructure/
│   │   └── Inventory.Api/
│   │
│   ├── Ordering/                         (Phase 7)
│   │   ├── Ordering.Domain/
│   │   ├── Ordering.Application/
│   │   ├── Ordering.Infrastructure/
│   │   └── Ordering.Api/
│   │
│   ├── Payment/                          (Phase 9)
│   │   └── Payment.Api/
│   │
│   └── Notification/                     (Phase 10)
│       └── Notification.Worker/
│
├── BuildingBlocks/                       (created only when justified — §10.4)
│   └── CommerceFlow.BuildingBlocks.Messaging/   (Phase 10)
│
tests/                                    (seeded Phase 7, expanded Phase 16)
├── Catalog.IntegrationTests/
├── Ordering.UnitTests/
└── Inventory.UnitTests/
```

### 10.2 The "no empty placeholders" rule

Only the projects needed by the current phase exist. After Phase 1, the solution contains **four projects and nothing else**. You should be able to open Solution Explorer at any moment and see exactly how far the system has grown.

### 10.3 Deviations from a uniform 4-project layout — and why

| Service | Projects | Reasoning |
|---|---|---|
| **Catalog** | 4 | Catalog's domain is genuinely thin (a Product with a price and an active flag). We still build 4 layers here **because learning the wiring is cheapest on a simple domain**. During Phase 1 I will explicitly flag which layers are earning their keep and which are ceremony — that honesty is part of the lesson. |
| **Identity** | 3 (no Domain) | The domain model is **owned by ASP.NET Core Identity** (`IdentityUser`, `IdentityRole`, `PasswordHasher`). Writing our own persistence-free `User` aggregate on top of that would be duplicated, meaningless indirection. Refresh-token rules are the only real logic and live in Application. |
| **Basket** | 1 (`Basket.Api`) | A basket is a per-user document with near-zero invariants ("quantity ≥ 1"). Four projects would produce four files of pass-through code. **This deviation is itself a teaching point:** Clean Architecture is a response to complexity, not a default. If Basket grows real rules, we split it and you will have felt the reason. |
| **Payment** | 1 (`Payment.Api`) | Payment is simulated. Its "domain" is a three-state enum. We split it only if it grows (e.g. gateway abstraction + retry policy). |
| **Inventory** | 4 | Real invariants (available/reserved arithmetic, reservation lifecycle, concurrency). Domain project earns its place. |
| **Ordering** | 4 | The reference implementation of the layering: aggregate, state machine, repository interface in Application, EF implementation in Infrastructure. |
| **Notification** | 1 Worker | Message consumer only. No controller, deliberately. |
| **Gateway** | 1 | Configuration, not code. |

### 10.4 BuildingBlocks rule

`BuildingBlocks` is **not** created in Phase 0. A project moves there only when **two or more services already have the same code** and that code is **purely technical** (messaging plumbing, error middleware, correlation-id handling).

**Never** put in BuildingBlocks: domain entities, DTOs shared between producer and consumer services, business rules. A shared model is the fastest way to turn microservices back into a distributed monolith.

Expected first legitimate BuildingBlock: `CommerceFlow.BuildingBlocks.Messaging` in Phase 10.

---

## 11. Dependency Rules

### 11.1 Reference direction for 4-project services

```text
        Api ──────────► Application ──────────► Domain
         │                    ▲
         │                    │
         └──► Infrastructure ─┘
              (implements Application's interfaces,
               references Domain for entity mapping)
```

- **Domain** references **nothing** (no ASP.NET Core, no EF Core, no SQLite, no HTTP, no Swagger, no messaging library, no `System.Text.Json` attributes).
- **Application** references **Domain** only. It defines interfaces (`IOrderRepository`) that Infrastructure implements.
- **Infrastructure** references **Application** and **Domain**. It contains `DbContext`, EF configurations, migrations, HTTP clients, message publishers.
- **Api** references **Application** (to call it) and **Infrastructure** (only to register implementations in DI at composition root).

### 11.2 The "Api references Infrastructure" question

An interviewer may say this violates the dependency rule. The answer: **the composition root is allowed to know everything**. `Program.cs` is where abstractions get bound to implementations; something must reference both. What matters is that **controllers and application code never use Infrastructure types**.

### 11.3 How we enforce it

Manually and visibly: before each phase closes, we inspect the `.csproj` files and confirm the reference graph. (An architecture test with NetArchTest is a Phase 16 option.)

---

## 12. Coding Standards

### 12.1 General

- C# latest, `nullable` enabled, `ImplicitUsings` enabled (and explained — the generated file lives in `obj/…GlobalUsings.g.cs`).
- `sealed` on classes not designed for inheritance.
- One type per file; file name = type name.
- `async`/`await` all the way down; **no** `.Result`, `.Wait()`, `async void`.
- `CancellationToken` accepted and forwarded on every I/O-bound method.
- `AsNoTracking()` on read-only queries — and a comment saying *why*.

### 12.2 Naming

**Good:** `ProductService`, `InventoryReservationService`, `CreateOrderRequest`, `OrderRepository`, `PaymentFailedIntegrationEvent`, `ReserveStockAsync`.

**Banned unless the name is literally the responsibility:** `Helper`, `Manager`, `Common`, `Utility`, `Processor`, `BaseService`, `Data`, `Info`.

Method names describe business intent: `Cancel`, `Reserve`, `Release`, `Deactivate`, `Refresh` — not `Process`, `Handle`, `Execute`, `DoAction`.

### 12.3 Comments

Comments explain **why**, never **what**.

```text
BAD:   // Get the product
GOOD:  // AsNoTracking: this endpoint only reads, so we skip change-tracker
       // snapshots and cut allocations on list endpoints.
```

Every non-obvious decision gets a one-line "why" comment. Every obvious line gets none.

### 12.4 Abstraction budget

Before any pattern is introduced, four questions must be answered in writing:

1. What concrete problem exists **right now**?
2. What does the pattern solve?
3. Why is it needed **now** rather than later?
4. What simpler alternative was rejected, and why?

**Pre-decided answers:**

| Pattern | Verdict | Reason |
|---|---|---|
| Generic repository `Repository<T>` | **No, never** | `DbContext` is already a Unit of Work and `DbSet<T>` is already a repository. Wrapping it loses `Include`, projections and `IQueryable` composition, and adds a layer you cannot justify. |
| Hand-written aggregate repository (`IOrderRepository`) | **Yes, Phase 7** | Ordering.Application must not reference EF Core; an order is loaded and saved as a whole aggregate. Narrow interface, 4–5 methods, no generics. |
| Repository in Catalog | **No** | Catalog's application services use an `ICatalogDbContext` interface owned by the Application layer (`DbSet<T>` + `SaveChangesAsync`). Fewer files, full LINQ composition and projections retained, no per-entity repository to maintain. Trade-off accepted: Application depends on EF Core's abstractions. |
| AutoMapper | **No** | Hand-written mapping methods. Explicit, debuggable, breaks at compile time when a property changes. |
| MediatR / CQRS | **No by default** | Reconsidered only in Ordering if handler count justifies it; we will discuss what it buys (pipeline behaviours) vs costs (indirection when debugging). |
| FluentValidation | **Only when needed** | DataAnnotations first; upgrade when validation becomes conditional/cross-field. |
| Result<T> pattern | **Discussed in Phase 7** | Phase 1 uses nullable returns + exceptions; we compare approaches once real business failures exist. |

### 12.5 Program.cs policy

`Program.cs` stays **explicit and readable** for the first services. Every registration is visible: controllers, Swagger, `DbContext`, DI, middleware, later authentication. We refactor into extension methods **only when a file becomes hard to read**, and when we do, we do it as a visible refactor with an explanation of what moved where. No magic on day one.

---

## 13. API Design Guidelines

### 13.1 Controllers only

Every externally callable HTTP operation lives in a controller action. No minimal-API endpoints for business functionality. Reasons: breakpoints land in a named method, routing/model binding/authorization attributes are visible in one place, and Solution Explorer answers "what does this service expose?" instantly.

```text
Controllers/
├── ProductsController.cs
└── CategoriesController.cs
```

Rules:
- `[ApiController]` + explicit `[Route("api/products")]`.
- `sealed class`, constructor injection, one logical resource per controller.
- Actions are **thin**: bind → call application service → translate to status code.
- Controllers must **not** contain EF Core queries, SQL, business rules, large mapping, or message publishing.

### 13.2 Resource → controller map (target state)

| Service | Controllers | Endpoints |
|---|---|---|
| Catalog | `ProductsController` | `GET /api/products`, `GET /api/products/{id}`, `POST /api/products`, `PUT /api/products/{id}`, `DELETE /api/products/{id}` (soft delete) |
| | `CategoriesController` | `GET /api/categories`, `GET /api/categories/{id}`, `POST /api/categories`, `PUT /api/categories/{id}`, `DELETE /api/categories/{id}` |
| Identity | `AuthController` | `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout` |
| | `UsersController` | `GET /api/users/me` |
| Basket | `BasketController` | `GET /api/basket`, `POST /api/basket/items`, `PUT /api/basket/items/{productId}`, `DELETE /api/basket/items/{productId}`, `DELETE /api/basket` |
| Inventory | `InventoryController` | `GET /api/inventory/{productId}`, `POST /api/inventory/adjustments` |
| | `InventoryReservationsController` | `POST /api/inventory/reservations`, `POST /api/inventory/reservations/{id}/release`, `POST /api/inventory/reservations/{id}/confirm` |
| Ordering | `OrdersController` | `POST /api/orders`, `GET /api/orders/{id}`, `GET /api/orders`, `GET /api/orders/my-orders`, `POST /api/orders/{id}/cancel` |
| Payment | `PaymentsController` | `POST /api/payments`, `GET /api/payments/{id}` |
| Notification | *(none)* | Message consumer only |
| Gateway | *(none)* | Routing configuration only |

### 13.3 CRUD is not the default

Expose **business intent**, not table operations.

- `Order` has **no** `PUT /api/orders/{id}` — an order is not an editable record. It has `POST /api/orders/{id}/cancel`.
- `Product` `DELETE` performs a **soft delete** (`IsActive = false`) because orders reference historical products and catalog rows must never vanish. Returns `204 No Content`.
- Inventory changes go through `adjustments` and `reservations`, never a raw quantity `PUT`.

### 13.4 DTO rules

Controllers never accept or return EF Core entities or domain entities.

```text
CreateProductRequest / UpdateProductRequest / ProductResponse
CreateCategoryRequest / UpdateCategoryRequest / CategoryResponse
PagedResponse<T>
```

Why: entities carry persistence and behaviour you don't want on the wire; over-posting protection; API contract can evolve independently of the schema; response shape is deliberate rather than accidental.

### 13.5 Status codes

| Code | Used when |
|---|---|
| `200 OK` | Successful read or update returning a body |
| `201 Created` | Resource created — with `Location` header via `CreatedAtAction` |
| `204 No Content` | Successful operation with nothing to return (soft delete, clear basket) |
| `400 Bad Request` | Request is malformed or fails input validation (`ValidationProblemDetails`) |
| `401 Unauthorized` | No/invalid token — *you are not identified* |
| `403 Forbidden` | Valid token, insufficient role — *you are identified but not allowed* |
| `404 Not Found` | Target resource does not exist (or is soft-deleted for non-admins) |
| `409 Conflict` | Rule violated by current state: duplicate SKU, insufficient stock, illegal state transition, concurrency clash |
| `422` | **Not used** — we keep `400` for validation to avoid ambiguity |
| `500` | Unhandled — logged, returned as a generic ProblemDetails with no stack trace |

Every endpoint's status codes get documented and justified when it is built.

### 13.6 Pagination contract

`GET /api/products?page=1&pageSize=20`

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 100,
  "totalPages": 5
}
```

`pageSize` is clamped server-side (max 100). Why pagination matters: unbounded list endpoints are the single most common cause of production memory and latency incidents, and it forces you to understand `Skip`/`Take` → SQL `LIMIT`/`OFFSET` and the extra `COUNT(*)` query.

---

## 14. Error Handling Strategy

Introduced progressively — no exception framework on day one.

| Phase | What exists |
|---|---|
| **Phase 1** | `[ApiController]` automatic `400 ValidationProblemDetails`; controllers return `NotFound()` for null results; a small `GlobalExceptionHandler : IExceptionHandler` registered with `AddProblemDetails()` + `AddExceptionHandler<>()` turns known application exceptions (e.g. `DuplicateSkuException`) into `409` ProblemDetails and everything else into a logged `500`. |
| **Phase 6** | Concurrency conflicts (`DbUpdateConcurrencyException`) → `409` with a retry-able message. |
| **Phase 7** | Domain exceptions for illegal state transitions → `409`; discussion of exceptions vs `Result<T>`. |
| **Phase 8** | Downstream HTTP failures: distinguish "downstream said no" (`400/409` passthrough with context) from "downstream is unavailable" (`503`). Timeouts explicitly configured. |
| **Phase 13** | Duplicate-request handling via idempotency keys → replay original response. |

Non-negotiables: never leak stack traces or SQL to clients; always log with the exception object as the first argument; always include a `traceId` in the ProblemDetails response (free with `AddProblemDetails()`).

---

## 15. Validation Strategy

Three distinct layers, deliberately kept separate:

**1. Input validation** (Api layer — DataAnnotations on request DTOs)
Shape only: `Name` required, length ≤ 200; `Price` > 0; `CategoryId` not empty; `Quantity` ≥ 1.
Runs automatically because of `[ApiController]`; produces `400 ValidationProblemDetails` before your action executes.

**2. Business/domain validation** (Domain or Application)
Rules requiring state or other data: "SKU must be unique", "cannot reserve more than available", "cannot cancel a confirmed order", "cannot confirm a cancelled order".
Result: `409 Conflict` (or `404` when the referenced thing does not exist).

**3. Database constraints** (Infrastructure)
Last line of defence: unique index on `Product.Sku`, unique index on `Category.Slug`, non-null columns, FK constraints **within** a service.
Why keep both #2 and #3: the app check gives a good error message; the DB constraint guarantees correctness under concurrency.

**Anti-pattern to avoid:** one giant validator that mixes all three. Each rule lives at the layer that owns the knowledge.

---

## 16. Logging Strategy

| Phase | Logging |
|---|---|
| 1 | `ILogger<T>` only. Structured message templates: `_logger.LogInformation("Creating product with SKU {Sku}", request.Sku);` — never string interpolation, because named properties survive into structured sinks. |
| 1 | Log levels used correctly: `Information` for business events, `Warning` for rejected-but-expected outcomes (duplicate SKU), `Error` for unhandled failures. Never log passwords, tokens, or full request bodies. |
| 5 | Log outbound HTTP calls: target service, duration, status. |
| 10 | Log published/consumed messages with message id and type. |
| 17 | Serilog with console + rolling file sinks, correlation id via middleware + `HttpClient` header propagation, request logging. OpenTelemetry as an optional stretch. |

---

## 17. Debugging Strategy

Debugging is a **first-class deliverable**, not an afterthought. Every phase ships with:

1. **A request-flow diagram** — Swagger → Controller → Application → DbContext → SQLite → Response.
2. **Named breakpoint locations** — file + method.
3. **A watch list** — the exact variables to inspect at each stop.
4. **Database verification steps** — which table, which row, which column.
5. **An explicit STOP** — no next phase until the developer confirms it works.

Standard tooling:
- Visual Studio **F5** with the service set as Startup Project.
- Swagger UI at the service's HTTPS URL.
- **DB Browser for SQLite** (free, no install ceremony) to inspect `.db` files.
- EF Core SQL logging enabled in Development so generated SQL is visible in the console.

The habit being trained: **never conclude "it works" from a green HTTP response alone** — confirm the row.

---

## 18. Testing Strategy

Incremental, and always after manual verification.

| Stage | When | What |
|---|---|---|
| Manual Swagger testing | Every phase | Primary verification method |
| Debugger stepping | Every phase | Understanding, not just verification |
| **Unit tests** | From Phase 6/7 | Only where real logic exists: inventory reservation arithmetic, order state transitions. Pure domain, no mocks, no database. |
| **Integration tests** | Phase 16 (Catalog first) | `WebApplicationFactory<Program>` + SQLite (file per test run, or `:memory:` with a held-open connection). Real HTTP pipeline, real EF Core, real SQL. |
| **Contract awareness** | Phase 16 | Tests asserting the JSON shape other services depend on |
| Architecture tests | Optional Phase 16 | NetArchTest asserting Domain references nothing |

Not doing: mocking `DbContext`, testing controllers with mocked everything, chasing coverage numbers.

Stack: **xUnit + Shouldly + Microsoft.AspNetCore.Mvc.Testing**.

---

## 19. Service Communication Roadmap

| Step | Phase | What | Lesson |
|---|---|---|---|
| 0 | 1–4 | No inter-service calls | Boundaries first |
| 1 | 5 | **Basket → Catalog** via typed `HttpClient` | `IHttpClientFactory`, typed clients, `BaseAddress` from config, timeouts, handling 404 vs unreachable, DTO duplication instead of shared library |
| 2 | 8 | **Ordering → Basket**, **Ordering → Inventory** | Orchestration over HTTP; partial failure; why "call three services in a row" is fragile |
| 3 | 9 | **Ordering → Payment** | Completes a synchronous saga-by-hand, including manual compensation |
| 4 | 10+ | Events replace fire-and-forget calls | Decoupling, availability, eventual consistency |

**Design rule:** synchronous calls are allowed for *reads that must be fresh* and *commands whose answer is needed now*. Everything else becomes an event. Service URLs always come from configuration (`appsettings.json`), never hard-coded.

---

## 20. Messaging Roadmap (Phase 10)

Concepts are taught **before** any package is installed:

1. **The problem synchronous HTTP creates** — Ordering is only as available as the slowest downstream service; a Notification outage must never fail an order; retry storms; temporal coupling.
2. **Vocabulary** — producer, consumer, queue, exchange, routing key, binding, acknowledgement (ack/nack), prefetch, dead-letter queue, at-least-once delivery.
3. **Command vs Event** — `ReserveStockCommand` (imperative, one handler) vs `OrderPlacedIntegrationEvent` (fact, N handlers). Events are named in the **past tense** and are immutable.
4. **Then, and only then**, implementation: publish `OrderPlacedIntegrationEvent` from Ordering; consume it in **Notification.Worker** (which just logs).

Notification is deliberately the **first consumer** because it is harmless: a bug there breaks nothing important, so all attention goes to understanding the broker.

Infrastructure decision (deferred to Phase 10): native Windows RabbitMQ install vs lifting the Docker ban.

---

## 21. Saga Roadmap (Phase 11)

**Preconditions:** Ordering, Inventory and Payment all exist and communicate (Phases 6–9), and messaging works (Phase 10).

Happy path:

```text
POST /api/orders  →  Order(Pending)
        ↓
Reserve inventory  →  Order(InventoryReserved)
        ↓
Process payment    →  Order(PaymentProcessing)
        ↓
Payment succeeded  →  Confirm reservation → Order(Confirmed)
```

Failure path with compensation:

```text
Order(Pending) → Reserve inventory → Order(InventoryReserved)
        ↓
Payment fails
        ↓
Release inventory reservation   ← compensation
        ↓
Order(PaymentFailed)
```

Teaching points: there is **no distributed transaction and no rollback** across databases — only *compensating actions* that are themselves business operations. Compensation can fail too, so it must be retryable and idempotent. Ordering is the **orchestrator** (chosen over choreography because the workflow is centralised, debuggable and visible in one place — the trade-off vs choreography gets discussed).

Saga state is persisted in Ordering's database, so a crash mid-workflow can be resumed.

---

## 22. Outbox Roadmap (Phase 12)

Problem is demonstrated **before** the solution is built:

```text
SaveChangesAsync()  ✔ order row committed
        ↓
process crashes / broker unreachable
        ↓
OrderPlaced event never published  →  inventory never reserved  →  silent data divergence
```

And the mirror-image failure: publish first, then the DB save fails → an event about an order that does not exist.

Solution: write the event into an **`OutboxMessages` table in the same transaction as the business data**, then a background publisher reads unpublished rows, publishes them, and marks them processed. One local transaction, atomic by construction.

Table shape: `Id`, `Type`, `Payload`, `OccurredAtUtc`, `ProcessedAtUtc`, `AttemptCount`, `Error`.

Consequence to teach: the outbox guarantees **at-least-once** publication, which makes duplicates a certainty — which is exactly why Phase 13 exists.

---

## 23. Idempotency Roadmap (Phase 13)

Two distinct problems, two solutions:

**(a) Duplicate message delivery (consumer side).** An `InboxMessages` / `ProcessedMessages` table keyed by message id; a consumer checks-then-records inside the same transaction as its work. Deduplication window and cleanup discussed.

**(b) Duplicate client requests (API side).** `POST /api/orders` with an `Idempotency-Key` header; the key + user + response are stored, and a repeat with the same key replays the original response instead of creating a second order. This is how real payment APIs work.

Also covered: designing operations to be **naturally idempotent** where possible (`ReleaseReservation` on an already-released reservation should succeed, not fail).

---

## 24. Security Roadmap

| Phase | Security capability |
|---|---|
| 2 | ASP.NET Core Identity, password hashing (never custom crypto), user registration, login |
| 2 | JWT access tokens (short-lived, HMAC-SHA256, signed with a config secret), claims: `sub`, `email`, `role`, `jti`, `exp` |
| 2 | Refresh tokens: separate table, **stored hashed**, single-use with rotation, revocable on logout |
| 3 | Catalog validates JWTs **locally** — no call to Identity. `[Authorize]` on writes, `[Authorize(Roles = "Admin")]` on admin operations, anonymous reads |
| 3 | Swagger `Authorize` button (Bearer scheme) so tokens can be tested from the UI |
| 4 | Basket ownership derived from the token's `sub` claim — **never** from route or body (broken object-level authorization is the #1 API vulnerability) |
| 7 | Orders scoped to the calling user; `GET /api/orders` (all) restricted to Admin |
| 15 | Gateway can terminate auth at the edge; internal services still validate (defence in depth) |
| Always | Secrets in `appsettings.Development.json` / user-secrets, never committed. `.gitignore` covers them. Documented as "in production this becomes a key vault + asymmetric signing" |

---

## 25. Phase-by-Phase Development Plan

### 25.1 Refinements to the proposed order (and why)

The proposed sequence was sound. Four changes:

| Change | Original | Refined | Reason |
|---|---|---|---|
| **Securing Catalog gets its own phase** | Auth added invisibly with Identity | **New Phase 3** | Retrofitting auth onto an existing service is a distinct, high-value lesson: JWT validation is *local*, `[Authorize]` vs `[Authorize(Roles)]`, 401 vs 403. It's a small phase with a big payoff. |
| **First HTTP call moves earlier** | "Phase 6 — service-to-service" after Ordering | **Phase 5**, right after Basket | The most natural first cross-service call is Basket asking Catalog for a product's name and price. Learning `HttpClient` on a two-service, low-risk call is far easier than learning it inside a three-service order flow. |
| **Notification moves earlier** | Phase 8, before messaging | **Inside Phase 10** | A Notification service that isn't consuming messages has nothing to do. As the *first* message consumer it's perfect: harmless, so all attention goes to the broker. |
| **Orchestration split from Ordering** | Ordering + comms merged | **Phase 7 (standalone) → Phase 8 (orchestration)** | Build the order aggregate and state machine in isolation first; add cross-service calls only once the domain is solid. Two failure surfaces, debugged separately. |

### 25.2 The roadmap

| Phase | Name | Projects created | Key learning |
|---|---|---|---|
| **0** | Solution setup & conventions | `.sln`, config files, git | Solution layout, MSBuild basics, conventions, tooling |
| **1** | **Catalog Service** | `Catalog.Domain/.Application/.Infrastructure/.Api` | Web API, Clean Architecture, EF Core, SQLite, migrations, DTOs, validation, filtering, pagination, Swagger, ProblemDetails, `ILogger` |
| **2** | Identity Service | `Identity.Application/.Infrastructure/.Api` | ASP.NET Core Identity, password hashing, JWT issuance, refresh-token rotation, roles |
| **3** | Secure Catalog | *(no new projects)* | JWT validation, `[Authorize]`, roles, 401 vs 403, Swagger auth, decentralized token validation |
| **4** | Basket Service | `Basket.Api` | Single-project justification, per-user data from claims, upsert semantics, `PUT` vs `POST` |
| **5** | Basket → Catalog (sync HTTP) | *(no new projects)* | Typed `HttpClient`, `IHttpClientFactory`, timeouts, config-driven URLs, downstream failure handling, contract duplication |
| **6** | Inventory Service | `Inventory.Domain/.Application/.Infrastructure/.Api` | Real invariants, reservation lifecycle, optimistic concurrency, `409` on conflict, first unit tests |
| **7** | Ordering Service (standalone) | `Ordering.Domain/.Application/.Infrastructure/.Api` | Aggregate design, encapsulated state machine, business-intent endpoints, hand-written repository, snapshots |
| **8** | Order orchestration over HTTP | *(no new projects)* | Multi-service orchestration, partial failure, manual compensation, why this hurts |
| **9** | Payment Service + integration | `Payment.Api` | Simulated payments via test tokens, completing the synchronous saga by hand |
| **10** | Messaging (RabbitMQ) + Notification | `BuildingBlocks.Messaging`, `Notification.Worker` | Broker concepts, publish/consume, ack, DLQ, a service with no HTTP endpoints |
| **11** | Saga / distributed order workflow | *(no new projects)* | Orchestration saga, compensation, persisted saga state |
| **12** | Transactional Outbox | *(no new projects)* | Atomic "save + publish", background dispatcher, at-least-once delivery |
| **13** | Idempotency | *(no new projects)* | Inbox table, `Idempotency-Key` header, naturally idempotent operations |
| **14** | *(reserved)* Hardening & cleanup | *(no new projects)* | Consolidate error handling, remove scaffolding, refactor `Program.cs` where justified |
| **15** | API Gateway | `CommerceFlow.ApiGateway` | YARP routing, single entry point, edge auth, why no logic lives here |
| **16** | Testing | `tests/*` | Domain unit tests, `WebApplicationFactory` integration tests, SQLite test databases |
| **17** | Observability | *(no new projects)* | Serilog, correlation ids across HTTP and messages, health checks, optional OpenTelemetry |

**Total: 18 phases.** Phases 3, 5, 8, 14 are deliberately small — a phase is a unit of understanding, not a unit of volume.

---

## 26. Acceptance Criteria per Phase

A phase is **not complete** until every box is ticked and the developer says so out loud.

### Phase 0
- [ ] `CommerceFlow.sln` exists and opens in Visual Studio
- [ ] Folder structure (`src/`, `src/Services/`, `docs/`, `tests/`) exists
- [ ] `.gitignore` excludes `bin/`, `obj/`, `*.db`, `*.db-wal`, `*.db-shm`, user secrets
- [ ] `.editorconfig` and `Directory.Build.props` exist and their contents are understood
- [ ] Git repository initialised, first commit made
- [ ] `README.md` contains the phase checklist
- [ ] `dotnet --version` shows .NET 10; `dotnet ef --version` works
- [ ] `dotnet build` succeeds (empty solution)

### Phase 1 — Catalog
- [ ] Solution builds with zero errors and zero warnings
- [ ] `Catalog.Api` starts; Swagger UI loads at the documented URL
- [ ] Migration created and applied; `catalog.db` exists at the documented path
- [ ] `Categories` and `Products` tables exist with expected columns, indexes and unique constraints
- [ ] Category can be created (`201`) and retrieved
- [ ] Product can be created (`201` with `Location` header)
- [ ] Product can be retrieved by id (`200`) and a missing id returns `404`
- [ ] Product can be updated (`200`)
- [ ] Product soft delete returns `204` and the row still exists with `IsActive = 0`
- [ ] Duplicate SKU returns `409` with ProblemDetails
- [ ] Invalid request (empty name, price ≤ 0) returns `400 ValidationProblemDetails`
- [ ] Filtering works: `search`, `categoryId`, `minPrice`/`maxPrice`, `isActive`
- [ ] Price range filtering returns **correct** results (validates the money conversion decision)
- [ ] Pagination works and `totalPages` is correct
- [ ] Generated SQL is visible in the console and understood
- [ ] Breakpoints hit in controller, application service and before `SaveChangesAsync`
- [ ] Rows verified in DB Browser for SQLite
- [ ] Developer can narrate the full request flow unaided

### Phase 2 — Identity
- [ ] `identity.db` created with ASP.NET Core Identity tables + `RefreshTokens`
- [ ] Register returns `201`; duplicate email returns `409`; weak password returns `400`
- [ ] Password is stored **hashed** — verified by inspecting `AspNetUsers`
- [ ] Login returns access token + refresh token; wrong password returns `401`
- [ ] JWT decoded at jwt.io shows expected claims (`sub`, `email`, `role`, `exp`)
- [ ] Refresh returns a new pair; the used refresh token is invalidated (rotation verified in the DB)
- [ ] Logout revokes the refresh token
- [ ] `GET /api/users/me` returns the caller; without a token returns `401`
- [ ] An Admin user exists (seeded) and carries the `Admin` role claim

### Phase 3 — Secure Catalog
- [ ] Catalog validates tokens **without calling Identity** (verified by stopping Identity and still succeeding)
- [ ] `GET` endpoints remain anonymous
- [ ] `POST/PUT/DELETE` without a token → `401`
- [ ] With a Customer token → `403`
- [ ] With an Admin token → success
- [ ] Swagger `Authorize` button works
- [ ] Tampered/expired token → `401`, and the reason is visible in logs

### Phase 4 — Basket
- [ ] `basket.db` created; basket + items tables correct
- [ ] `GET /api/basket` returns an empty basket for a new user
- [ ] Adding an item creates it; adding the same product again **increments** rather than duplicating
- [ ] Quantity update and item removal work; clear basket returns `204`
- [ ] Two different users' tokens produce two isolated baskets
- [ ] Passing another user's id in the body/route does **not** affect their basket
- [ ] Quantity < 1 returns `400`

### Phase 5 — Basket → Catalog
- [ ] Adding an item validates the product against Catalog
- [ ] Unknown product id → `400/404` with a clear message
- [ ] Catalog stopped → basket read still works (degraded) and add returns `503`, not `500`
- [ ] Timeout configured and demonstrably triggered
- [ ] Catalog's base URL comes from configuration
- [ ] Outbound call visible in logs with duration and status
- [ ] Breakpoints hit in both services in a single debug session

### Phase 6 — Inventory
- [ ] `inventory.db` created; item + reservation tables correct
- [ ] Stock adjustment increases available quantity
- [ ] Reservation moves quantity available → reserved; response includes reservation id
- [ ] Reserving more than available → `409`, no partial change
- [ ] Release returns quantity to available; confirm consumes it
- [ ] Releasing an already-released reservation behaves predictably (documented)
- [ ] Concurrency conflict reproduced deliberately and returns `409`
- [ ] Domain unit tests pass

### Phase 7 — Ordering (standalone)
- [ ] `ordering.db` created; `Orders` + `OrderItems` correct
- [ ] Order created in `Pending` with a computed total
- [ ] Order retrieved by id; another user's order returns `404`/`403` per the documented rule
- [ ] `GET /api/orders/my-orders` returns only the caller's orders
- [ ] Cancel works from allowed states and returns `409` from disallowed states
- [ ] No `PUT /api/orders/{id}` exists — and the developer can explain why
- [ ] Order state cannot be set arbitrarily from outside the aggregate
- [ ] Domain unit tests cover every legal and illegal transition

### Phase 8 — Orchestration
- [ ] Placing an order reads the basket, reserves inventory, and clears the basket
- [ ] Order reaches `InventoryReserved`
- [ ] Insufficient stock → order rejected, basket untouched, `409` returned
- [ ] Inventory stopped mid-flow → order does not silently succeed; failure is visible and recorded
- [ ] Developer can articulate at least three failure modes this design has

### Phase 9 — Payment
- [ ] `payment.db` created
- [ ] Success token → payment succeeded → order `Confirmed`, reservation confirmed
- [ ] Failure token → payment failed → **reservation released**, order `PaymentFailed`
- [ ] Stock returns to its original level after a failed payment (verified in the DB)
- [ ] The full happy path and the full failure path are both reproducible on demand

### Phase 10 — Messaging + Notification
- [ ] Broker running and reachable; management UI accessible
- [ ] `OrderPlacedIntegrationEvent` published on order placement
- [ ] `Notification.Worker` consumes it and logs
- [ ] Worker stopped → message waits in the queue; worker restarted → message is processed
- [ ] Consumer exception → message nacked and lands in the dead-letter queue
- [ ] Ordering succeeds even with Notification down

### Phase 11 — Saga
- [ ] Full happy path runs asynchronously end-to-end
- [ ] Payment failure triggers inventory release automatically
- [ ] Saga state persisted and visible in the database
- [ ] Killing a service mid-saga leaves recoverable, explainable state

### Phase 12 — Outbox
- [ ] `OutboxMessages` table exists
- [ ] Event row written in the same transaction as the order
- [ ] Broker stopped at publish time → order still saved, message pending → broker restarted → message published
- [ ] Processed messages marked, failures retried with attempt counts

### Phase 13 — Idempotency
- [ ] Same `Idempotency-Key` sent twice creates exactly one order and replays the original response
- [ ] Redelivered message processed exactly once (verified via `ProcessedMessages`)
- [ ] Duplicate release of a reservation does not double-restore stock

### Phase 15 — Gateway
- [ ] All services reachable through `:5100`
- [ ] Routes documented; no business logic in the gateway
- [ ] Auth still enforced at both edge and service

### Phase 16 — Testing
- [ ] `dotnet test` green
- [ ] Domain unit tests for Ordering and Inventory
- [ ] Catalog integration tests exercise the real pipeline against a throwaway SQLite database
- [ ] Tests are independent and repeatable

### Phase 17 — Observability
- [ ] Structured logs to console + file
- [ ] A single correlation id traceable across services and messages
- [ ] `/health` endpoints report database (and broker) status

---

## 27. Debug Checklist per Phase

Every phase ships a checklist in this shape. The Phase 1 example is authoritative for format:

### Example — Phase 1, `POST /api/products`

**Request flow to memorise**

```text
Swagger Request
      ↓  HTTP POST /api/products
Kestrel → Middleware pipeline
      ↓
Routing → ProductsController.Create
      ↓  model binding + [ApiController] validation
CreateProductRequest
      ↓
ProductService.CreateAsync          (Application)
      ↓  domain object created here
Product                             (Domain)
      ↓
CatalogDbContext.Products.Add / SaveChangesAsync   (Infrastructure)
      ↓  INSERT INTO Products ...
SQLite (catalog.db)
      ↓
201 Created + Location header
```

**Steps**

1. Set `Catalog.Api` as Startup Project.
2. Press **F5**. Confirm Swagger opens at the documented HTTPS URL.
3. `GET /api/products` → expect `200` with an empty page result.
4. `POST /api/categories` → create a category, copy its `id`.
5. `POST /api/products` with that `categoryId` → expect `201`, check the `Location` response header.
6. Open `catalog.db` in DB Browser → confirm the row and the stored price representation.
7. Set breakpoints: `ProductsController.Create`, `ProductService.CreateAsync`, the line before `SaveChangesAsync`.
8. Re-send the request; step through with **F11**/**F10**.
9. Inspect: `request`, `product`, `product.Id`, `product.Sku`, `product.Price`, `cancellationToken.IsCancellationRequested`.
10. In the console, read the generated `INSERT` statement.
11. `GET /api/products/{id}` with the new id → `200`.
12. `POST` the same SKU again → `409` ProblemDetails.
13. `POST` with `price: 0` → `400 ValidationProblemDetails`; confirm the breakpoint in the controller is **not** hit (validation short-circuits) and explain why.

Later phases add multi-process debugging (attach to two services, follow a call across the boundary), broker inspection (management UI queue depth), and table inspection (`OutboxMessages`, `ProcessedMessages`).

---

## 28. Interview Questions per Phase

Asked at the end of each phase, about the code just written.

**Phase 1 — Catalog**
- 30-second pitch: *"What does your Catalog Service do?"*
- Why is Catalog separate from Inventory?
- Why does `Catalog.Domain` reference nothing?
- Why does `Catalog.Api` reference `Catalog.Infrastructure` — doesn't that break the dependency rule?
- What is `[ApiController]` actually doing for you? Name three behaviours.
- `ActionResult<T>` vs `IActionResult` — when does the difference matter?
- What does `CreatedAtAction` produce, and why `201` rather than `200`?
- Why DTOs instead of returning EF entities?
- What does `AsNoTracking()` change internally?
- Why is `DbContext` registered as `Scoped`?
- How does deferred execution let you compose filters conditionally? When does the query actually run?
- Why did we store money as integer minor units on SQLite?
- Soft delete vs hard delete — defend the choice.
- `409` vs `400` for a duplicate SKU — justify.
- Why does every async method take a `CancellationToken`?
- What SQL did `Skip`/`Take` produce, and what is the cost of `OFFSET` on large tables?

**Phase 2 — Identity**
- What's inside a JWT? What are the three parts?
- Why doesn't Catalog call Identity to validate a token?
- Why are refresh tokens needed if you already have an access token?
- Why store refresh tokens hashed? Why rotate them?
- Where does authentication end and authorization begin in the middleware pipeline?
- Why never write your own password hashing?

**Phase 3 — Securing Catalog**
- `401` vs `403` — precise difference.
- What does `[Authorize(Roles = "Admin")]` check, and where does the role come from?
- What happens if two services use different signing keys?

**Phase 4–5 — Basket & first HTTP call**
- Why take the user id from the token instead of the request body?
- Why doesn't Basket share a DTO library with Catalog?
- What problem does `IHttpClientFactory` solve that `new HttpClient()` does not?
- Catalog is down — what should the basket endpoint return, and why?

**Phase 6 — Inventory**
- What is optimistic concurrency? How did you implement it without `rowversion`?
- Why is "available" separate from "reserved"?
- Two customers, one unit left — walk me through what happens.

**Phase 7 — Ordering**
- Why is there no `PUT /api/orders/{id}`?
- How does the aggregate prevent an invalid state transition?
- Why does the order store a copy of the product name and price?
- Why does Ordering have a repository when Catalog does not?

**Phase 8–9 — Orchestration & Payment**
- What happens if inventory is reserved and the process dies before payment?
- Why can't you use a database transaction across Ordering and Inventory?

**Phase 10–13 — Messaging, Saga, Outbox, Idempotency**
- Command vs event — name one of each from your system.
- What is at-least-once delivery and what does it force you to build?
- Why can't you just publish the event right after `SaveChangesAsync`?
- Orchestration vs choreography — which did you pick and why?
- What makes an operation idempotent? Give an example from your code.
- Where in your system is data eventually consistent, and what is the user-visible effect?

**Phase 15–17 — Gateway, Testing, Observability**
- What does the gateway do, and what must it never do?
- What do your integration tests cover that unit tests cannot?
- How do you trace one request across four services?

---

## 29. Git Commit Strategy

- Repository initialised in Phase 0; `main` branch; commits go straight to `main` (solo project, linear history reads like real development).
- **Conventional Commits**, one commit per meaningful, working step — never one giant commit per phase.
- Tag each completed phase: `phase-01-catalog`, `phase-02-identity`, …

```text
chore: initialise solution structure and conventions
feat(catalog): add Product and Category domain entities
feat(catalog): add CatalogDbContext and EF Core configurations
feat(catalog): add initial migration and SQLite connection
feat(catalog): add category endpoints
feat(catalog): add product endpoints
feat(catalog): add filtering and pagination to product listing
feat(catalog): add global exception handling with ProblemDetails
docs(catalog): document endpoints and debug checklist
test(catalog): add integration tests for product endpoints
```

Rules: never commit a non-building solution; never commit `.db` files or secrets; commit message body explains **why** when the change is non-obvious.

---

## 30. Definition of Done

### Per phase
1. Code compiles with **zero warnings**.
2. Every acceptance criterion for the phase is ticked.
3. Every endpoint manually tested in Swagger, including failure cases.
4. Database rows verified by direct inspection.
5. Breakpoints hit; the developer stepped through the main flow at least once.
6. The developer can narrate the request flow **without looking at the code**.
7. Interview questions for the phase answered correctly.
8. Committed with conventional commit messages and tagged.
9. `README.md` phase checklist updated.
10. The developer explicitly says **"Phase X working"**.

### Per project (final)
- All 18 phases complete.
- `dotnet test` green.
- README documents how to run every service, in order, from a clean clone.
- The developer can deliver the 30-second pitch for each service and defend every architectural decision in Appendix C.

---

## Appendix A — Port Table

| Service | HTTP | HTTPS | Database file |
|---|---:|---:|---|
| API Gateway | 5100 | 7100 | — |
| Catalog | 5101 | 7101 | `catalog.db` |
| Identity | 5102 | 7102 | `identity.db` |
| Basket | 5103 | 7103 | `basket.db` |
| Inventory | 5104 | 7104 | `inventory.db` |
| Ordering | 5105 | 7105 | `ordering.db` |
| Payment | 5106 | 7106 | `payment.db` |
| Notification.Worker | — | — | *(none initially)* |
| RabbitMQ (Phase 10) | 5672 | — | management UI: 15672 |

**Convention:** HTTP `51xx`, HTTPS `71xx`, gateway takes `x00`. Ports are set explicitly in each service's `Properties/launchSettings.json` under `applicationUrl` — never left to the template's random assignment. Phase 1 includes locating and reading that file.

Swagger URL for any service: `https://localhost:<https-port>/swagger`.

---

## Appendix B — Working Agreement

**How the assistant must behave during implementation.** (This becomes `CLAUDE.md` in Phase 0 so it applies to every future session.)

1. **One phase at a time. One microservice at a time.** Never build ahead.
2. **Never create placeholder projects** for future services.
3. Every phase follows this flow:
   - **Step 1 — Explain the goal:** what we're building, why, what you'll learn, how it fits.
   - **Step 2 — Show the structure:** only the projects for this phase, with a reason for each.
   - **Step 3 — Create files in small groups**, explaining after each group. Never 50 files in one response. Order: domain → DTOs → application → persistence → controller.
   - **Step 4 — Migration + database walkthrough**, including the conceptual SQL.
   - **Step 5 — Manual test guide** with example request bodies and expected status codes.
   - **Step 6 — Debug checklist** with breakpoint locations and a watch list.
   - **Step 7 — Interview questions.**
   - **Step 8 — STOP.**
4. **Critical stop rule.** Every phase ends with: *"Phase X implementation is complete. Run and debug it using the checklist above. Do not continue to the next microservice yet. Send me the error if something fails, or say 'Phase X working' when everything is successful."* Then stop. Do not begin the next phase.
5. **Error mode.** When given a compiler error, runtime error, exception, screenshot or migration failure, switch to debugging and stop adding features. Answer in this structure: **Problem → Likely Cause → Fix (smallest possible change) → Why It Happened → Verify**.
6. **No magic.** Show explicit configuration first (`AddDbContext` with `UseSqlite` inline in `Program.cs`); refactor into extension methods later, as a visible, explained refactor.
7. **Justify every abstraction** with the four questions in §12.4. If it isn't needed yet, don't add it.
8. **Controllers for all HTTP endpoints.** No minimal APIs for business functionality.
9. **Explain deviations** from this PRD whenever they occur.
10. **Never assume it works.** Always finish with verification steps that include inspecting the database.

---

## Appendix C — Key Architectural Decisions (ADR log)

| # | Decision | Alternatives rejected | Rationale |
|---|---|---|---|
| 1 | Catalog is built first | Identity first | Catalog has no auth, no distributed transactions, no messaging, no dependencies. It teaches the full vertical slice (API → EF → SQLite) with the fewest moving parts, and every later service reuses that skeleton. Identity first would front-load JWT complexity before you'd ever seen the project's own patterns. |
| 2 | .NET 10 controllers, not Minimal APIs | Minimal APIs | Breakpoints in named methods, visible routing/binding/authorization, discoverable in Solution Explorer, and it matches most enterprise codebases you'll be interviewed about. |
| 3 | SQLite everywhere, one file per service | Shared SQLite; SQL Server LocalDB | Zero install, inspectable, disposable; database-per-service preserved so the architecture lesson survives the simplification. |
| 4 | Money stored as integer minor units via value converter | `decimal` → TEXT (default); `decimal` → `double` | EF Core's SQLite provider cannot correctly order/compare TEXT-stored decimals, which would silently break price-range filters; `double` is not safe for money. |
| 5 | Inventory concurrency via `int Version` token | `rowversion`; pessimistic locking | SQLite has no `rowversion`; an explicit token is provider-agnostic and makes the mechanism visible. |
| 6 | Products are soft-deleted | Hard delete | Orders reference historical products; catalog rows must not vanish. **Refined in Phase 1:** we deliberately did *not* add an EF global query filter (`HasQueryFilter(p => p.IsActive)`). A global filter would make inactive rows invisible to every query, which would silently break both the `isActive=false` filter and the "verify the row still exists after DELETE" debugging step. `IsActive` stays an explicit, optional filter until Phase 3 splits public reads from admin reads. |
| 7 | No generic repository. Catalog's Application layer talks to an `ICatalogDbContext` interface it owns; Ordering gets a hand-written `IOrderRepository` in Phase 7 | Repository everywhere; repository nowhere | `DbContext` is already UoW + repository. **Refined in Phase 1:** application services cannot reference the concrete `CatalogDbContext` (that lives in Infrastructure), so Application defines `ICatalogDbContext` exposing `DbSet<T>` + `SaveChangesAsync`. Cost, stated honestly: `Catalog.Application` now references the EF Core package (not SQLite, not Infrastructure). `Catalog.Domain` still references nothing, which is what actually protects the business rules. Ordering will take the opposite approach so the two can be compared. |
| 8 | No AutoMapper, no MediatR, no CQRS by default | Using them from day one | Both hide the call stack; hidden call stacks are the enemy of debugging and of interview explanations. Revisit only when a concrete problem appears. |
| 9 | Identity has no Domain project | 4-project uniformity | ASP.NET Core Identity owns the user model; a parallel hand-written domain would be pure duplication. |
| 10 | Basket and Payment are single-project services | 4-project uniformity | Clean Architecture answers complexity. Neither service has enough invariants to justify the layers. Uniformity for its own sake is cargo-culting. |
| 11 | Basket uses SQLite, not Redis | Redis | Redis is the right production answer but adds an install and hides data. The persistence layer is isolated so the swap is a one-class change. |
| 12 | Payments simulated with explicit test tokens (`tok_success`, `tok_failure`, `tok_timeout`) | "amount ending in .13"; random failures | Deterministic, self-documenting, and mirrors how Stripe's test tokens actually work. Magic amounts surprise the next reader. |
| 13 | Ordering is the saga **orchestrator** | Choreography | The workflow is centralised and debuggable in one place — essential while learning. Choreography's trade-offs are discussed in Phase 11. |
| 14 | Manual `dotnet ef database update`; no auto-migrate on startup | `Database.Migrate()` in `Program.cs` | Migrations stay a visible, deliberate act; also the correct production posture with multiple instances. |
| 15 | BuildingBlocks created only on demand | Created in Phase 0 | An empty shared library invites premature sharing, and shared domain code re-couples microservices into a distributed monolith. |
| 16 | Notification has no controller | Uniform Web API projects | Architecture should reflect responsibility. "Not every microservice is a Web API" is a lesson worth the asymmetry. |
| 17 | Securing Catalog is its own phase (3) | Folded into Phase 2 | Retrofitting auth is a distinct skill; isolating it makes 401/403 and local token validation unmissable. |
| 18 | First cross-service HTTP call is Basket → Catalog (Phase 5) | Waiting until Ordering | Learn `HttpClient` on the simplest possible two-service call, not inside a three-service order flow. |

---

*End of PRD v1.0. Phase 0 begins only on the explicit instruction "Start Phase 0".*
