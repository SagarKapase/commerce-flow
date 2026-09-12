# CommerceFlow

A distributed e-commerce backend built with ASP.NET Core microservices, .NET 10, EF Core and SQLite.

Built **one microservice at a time**, with the goal that every file can be explained in an interview.
No frontend — Swagger is the interface. No Docker, no cloud, no message broker until it is earned.

- Full plan: [docs/PRD.md](docs/PRD.md)
- Working agreement: [CLAUDE.md](CLAUDE.md)

## Development log

- [x] **Phase 0** — Solution setup and conventions
- [x] **Phase 1** — Catalog Service
- [x] **Phase 2** — Identity Service
- [x] **Phase 3** — Secure Catalog with JWT
- [x] **Phase 4** — Basket Service
- [x] **Phase 5** — Basket → Catalog (synchronous HTTP)
- [x] **Phase 6** — Inventory Service
- [x] **Phase 7** — Ordering Service (standalone)
- [x] **Phase 8** — Order orchestration over HTTP
- [x] **Phase 9** — Payment Service
- [x] **Phase 10** — API Gateway
- [x] **Phase 11** — Messaging (RabbitMQ) + Notification Worker
- [ ] **Phase 12** — Saga / distributed order workflow
- [ ] **Phase 13** — Transactional Outbox
- [ ] **Phase 14** — Idempotency
- [ ] **Phase 15** — Hardening and cleanup
- [ ] **Phase 16** — Testing
- [ ] **Phase 17** — Observability

## Services and ports

| Service | HTTP | HTTPS | Database | Status |
|---|---:|---:|---|---|
| Catalog | 5101 | 7101 | `catalog.db` | Built |
| Identity | 5102 | 7102 | `identity.db` | Built |
| Basket | 5103 | 7103 | `basket.db` | Built |
| Inventory | 5104 | 7104 | `inventory.db` | Built |
| Ordering | 5105 | 7105 | `ordering.db` | Built |
| Payment | 5106 | 7106 | `payment.db` | Built |
| Notification | — | — | — | Built (worker, no HTTP) |
| API Gateway | 5100 | 7100 | — | Built |

## Prerequisites

- .NET 10 SDK — verify with `dotnet --version`
- EF Core tools — `dotnet tool install --global dotnet-ef`
- [DB Browser for SQLite](https://sqlitebrowser.org/) to inspect `.db` files

## Running the Catalog Service

```bash
# From the repository root
dotnet run --project src/Services/Catalog/Catalog.Api --launch-profile https
```

Swagger UI: <https://localhost:7101/swagger>

### Applying migrations

```bash
dotnet ef database update \
  --project src/Services/Catalog/Catalog.Infrastructure \
  --startup-project src/Services/Catalog/Catalog.Api
```

The database file is created at `src/Services/Catalog/Catalog.Api/catalog.db`.

## Running the Identity Service

```bash
dotnet run --project src/Services/Identity/Identity.Api --launch-profile https
```

Swagger UI: <https://localhost:7102/swagger>

```bash
dotnet ef database update \
  --project src/Services/Identity/Identity.Infrastructure \
  --startup-project src/Services/Identity/Identity.Api
```

The `Admin` and `Customer` roles are seeded by the migration. A development
administrator (`admin@commerceflow.local` / `Admin#12345`, from
`appsettings.Development.json`) is created at startup in Development only.

## Authentication between services (Phase 3)

Catalog validates Identity's tokens **locally** — it never calls Identity. Both
services hold the same `Jwt:SigningKey` in `appsettings.Development.json`, and
agree on `Jwt:Issuer` and `Jwt:Audience`. Nothing is shared in code.

| Catalog endpoints | Authorization |
|---|---|
| `GET /api/products`, `GET /api/products/{id}` | anonymous |
| `GET /api/categories`, `GET /api/categories/{id}` | anonymous |
| all `POST` / `PUT` / `DELETE` | `Admin` role |

To call a protected endpoint: log in at Identity, copy the `accessToken`, and
paste it into Catalog's Swagger **Authorize** button (no `Bearer ` prefix).

Because the key is symmetric, every service that validates a token also holds
the key that creates one. Production would use asymmetric RS256 signing so that
only Identity can mint tokens.

## Running the Basket Service

```bash
dotnet run --project src/Services/Basket/Basket.Api --launch-profile https
```

Swagger UI: <https://localhost:7103/swagger> — every endpoint needs a token.

```bash
# One project, so --startup-project can be omitted entirely
dotnet ef database update --project src/Services/Basket/Basket.Api
```

The basket owner is always taken from the token's `sub` claim. There is no user
id in any route, query string or request body, so one user cannot address
another user's basket.

## Service-to-service calls (Phase 5)

Basket calls Catalog over HTTP through a typed `HttpClient`. `POST /api/basket/items`
takes only `productId` and `quantity`; the name and price are fetched from Catalog,
which owns them.

```
Basket  --HTTP GET /api/products/{id}-->  Catalog
```

Catalog's address is configuration, not code:

```json
"Services": { "Catalog": { "BaseUrl": "https://localhost:7101", "TimeoutSeconds": 5 } }
```

**Catalog must be running to add an item.** What happens when it isn't:

| Basket endpoint | Catalog down |
|---|---|
| `GET /api/basket` | `200` — reads use the snapshot, no external dependency |
| `POST /api/basket/items` | `503` + `Retry-After: 5` |

Prices are snapshotted when an item is added and deliberately **not** refreshed on
read, so a customer can always see their basket. They are re-checked at order time
(Phase 8).

In Development the client accepts the self-signed dev certificate so the phase works
on a fresh machine. Run `dotnet dev-certs https --trust` once and that block in
`Program.cs` can be deleted.

## Running the Inventory Service

```bash
dotnet run --project src/Services/Inventory/Inventory.Api --launch-profile https

dotnet ef database update \
  --project src/Services/Inventory/Inventory.Infrastructure \
  --startup-project src/Services/Inventory/Inventory.Api
```

Swagger UI: <https://localhost:7104/swagger>

Stock lives in two buckets: `AvailableQuantity` (anyone may buy) and
`ReservedQuantity` (promised to an order that has not been paid for).
`Total = Available + Reserved`.

| Operation | Available | Reserved | Total |
|---|---|---|---|
| Reserve | ↓ | ↑ | unchanged |
| Release | ↑ | ↓ | unchanged |
| Confirm | — | ↓ | **↓** |
| Adjust  | ↕ | — | ↕ |

A reservation covers many products and is **all-or-nothing** — one transaction, one
outcome. Its lifecycle is `Held → Confirmed` or `Held → Released`, both terminal.
Confirm and release are **idempotent**: calling either twice succeeds and moves stock
once.

Overselling is prevented twice over: a domain check (`Reserve` refuses more than is
available) and an EF **optimistic concurrency token** on `InventoryItem.Version`, which
adds `AND Version = @original` to every `UPDATE`.

## Running the Ordering Service

```bash
dotnet run --project src/Services/Ordering/Ordering.Api --launch-profile https

dotnet ef database update   --project src/Services/Ordering/Ordering.Infrastructure   --startup-project src/Services/Ordering/Ordering.Api
```

Swagger UI: <https://localhost:7105/swagger>

An order is a record of a commitment, so the API exposes business operations,
not CRUD. **There is no `PUT`** — and the internal transitions have no endpoints
at all, because nobody should be able to tell us their own payment succeeded.

```
POST /api/orders              place        -> Pending
GET  /api/orders/{id}         own or Admin -> 404 if neither
GET  /api/orders/my-orders    the caller's history
GET  /api/orders              Admin only, paged
POST /api/orders/{id}/cancel  Pending or InventoryReserved only
```

State machine — four terminal states, and nothing leaves them:

```
                 ┌── reject ──> Rejected
Pending ─────────┼── cancel ──> Cancelled
   └─ reserve ──> InventoryReserved ── cancel ──> Cancelled
                        └─ startPayment ──> PaymentProcessing ─┬─> Confirmed
                                                               └─> PaymentFailed
```

## Placing an order (Phase 8)

`POST /api/orders` takes **only a shipping address**. Everything else is derived
server-side:

```
POST /api/orders
  1. GET    Basket    /api/basket             <- the items and prices
  2. SAVE   Ordering  order as Pending        <- durable record FIRST
  3. POST   Inventory /reservations           <- hold the stock
  4. SAVE   Ordering  InventoryReserved|Rejected
  5. DELETE Basket    /api/basket             <- best effort
```

Ordering **forwards the caller's token** on every outbound call, so Basket still
derives the owner from `sub` and nobody can order from someone else's basket.
A `DelegatingHandler` attaches it, so no client method mentions authentication.

**Five services must be running:** Catalog, Identity, Basket, Inventory, Ordering.

| Situation | Result |
|---|---|
| Happy path | `201`, status `InventoryReserved`, basket cleared |
| Empty basket | `400` |
| Not enough stock | `201` with status **`Rejected`** and a reason — the order records the failed attempt |
| Inventory down | `503` + `Retry-After` — **and a `Pending` order is left behind** |
| Basket down | `503`, and no order is created at all |

That `Pending` ghost order is deliberate and instructive: there is no transaction
across three databases. Step 2 saves before step 3 precisely so the failure leaves
a *visible, recoverable* row instead of stock held for an order that does not exist.
Phase 11 makes the flow resumable, Phase 12 guarantees the next step is never lost,
Phase 13 makes retrying the reservation safe.

## Paying for an order (Phase 9)

```
POST /api/orders/{id}/pay   { "paymentMethodToken": "tok_success" }

  1. SAVE   Ordering   PaymentProcessing            <- before any money moves
  2. POST   Payment    /api/payments                <- amount from the ORDER
  3a. paid     -> POST Inventory /reservations/{id}/confirm -> Confirmed
  3b. declined -> POST Inventory /reservations/{id}/release -> PaymentFailed
```

Step 3b is a **compensating action** — the undo half of a distributed transaction
that has no undo. It is not a rollback: the reservation row stays, now `Released`,
and the order stays, now `PaymentFailed`. Both facts remain true forever.

**Test tokens** for the simulated gateway:

| Token | Outcome |
|---|---|
| `tok_success` | Charged → order `Confirmed`, stock consumed |
| `tok_declined` | Declined → stock **released**, order `PaymentFailed` |
| `tok_insufficient_funds` | Same, different reason |
| `tok_timeout` | Gateway takes 10s; Ordering waits 5s → `503` |

A payment is **charged at most once per order**, enforced by a unique index on
`Payments.OrderId`. Calling `POST /api/payments` twice returns the first payment
unchanged — idempotency by natural key.

**Known inconsistency, deliberately reproducible with `tok_timeout`:** the charge
completes while the caller times out, leaving Payment `Succeeded`, the order
`PaymentProcessing` and the stock still held. Nothing detects it. That is what
Phase 12's saga and Phase 14's idempotency are for.

## The API Gateway (Phase 10)

```bash
dotnet run --project src/Gateway/CommerceFlow.ApiGateway --launch-profile https
```

One door on **https://localhost:7100**. YARP, configured entirely from
`appsettings.json` — **no controllers, no DTOs, no database**. Read that config
file: what it omits is the point.

| Path | Routes to | Gateway auth |
|---|---|---|
| `/api/auth/**` | Identity | anonymous |
| `/api/users/**` | Identity | authenticated |
| `/api/products/**`, `/api/categories/**` | Catalog | anonymous (Catalog decides per verb) |
| `/api/basket/**` | Basket | authenticated |
| `/api/orders/**` | Ordering | authenticated |
| `/api/inventory/{productId:guid}` | Inventory | authenticated |
| `/api/inventory/adjustments` | Inventory | authenticated |
| **`/api/inventory/reservations/**`** | **nothing — 404** | — |
| **`/api/payments/**`** | **nothing — 404** | — |

The last two rows close the holes flagged in Phases 6 and 9. **Internal services
stay internal because nothing outside can address them** — no new code, no new
credential, no check anyone can forget. Ordering still reaches both, because
service-to-service traffic goes direct and never passes through the gateway.

Honest limitation: on one laptop every port is still reachable, so
`https://localhost:7106/api/payments` works if you call it directly. The gateway
removes the *door*; the *wall* is the network, and a dev machine has none.

Also at the edge: **rate limiting** (100 requests/minute, partitioned by user id
falling back to IP, `429` + `Retry-After`) and **coarse authentication** — the
gateway rejects obviously-anonymous traffic one hop early while every service
still validates for itself.

## Messaging (Phase 11)

Uses the RabbitMQ container already on this machine, in its **own vhost** so it
cannot collide with anything else there.

```bash
docker start commerceflow-rabbitmq          # management UI: http://localhost:15672
dotnet run --project src/Services/Notification/Notification.Worker
```

The broker password is in **user secrets**, not in any committed file:

```bash
dotnet user-secrets set "RabbitMq:Password" "<password>" --project src/Services/Ordering/Ordering.Api
dotnet user-secrets set "RabbitMq:Password" "<password>" --project src/Services/Notification/Notification.Worker
```

**Topology, declared from code at start-up** (nothing is created by hand):

```
Ordering ──publish──> [commerceflow.events]  topic exchange
                              │ order.#
                              v
                    notification.order-events   durable queue, prefetch 10
                              │ on failure (nack, requeue:false)
                              v
                     [commerceflow.dlx]  direct exchange
                              v
                  notification.order-events.dlq
```

| Event | Routing key |
|---|---|
| `OrderPlacedIntegrationEvent` | `order.placed` |
| `OrderConfirmedIntegrationEvent` | `order.confirmed` |
| `OrderPaymentFailedIntegrationEvent` | `order.payment-failed` |

`Notification.Worker` is a **`Microsoft.NET.Sdk.Worker` project** — no Kestrel, no
port, no controller, and no gateway route, because there is nothing to route to.
*Not every microservice is a Web API.*

Ordering does not know it exists: no client, no base URL, no timeout, no retry
policy. Stop the worker and orders still complete; the messages wait in the queue
and are delivered when it returns.

**Known gap, reproducible:** stop the broker and place an order — the order
succeeds and the event is **lost forever** (`EVENT LOST` in Ordering's log). The
database commit and the publish are two operations with no transaction around
them, and no retry fixes that. Phase 13's transactional outbox does.

## Shared code

`src/BuildingBlocks/CommerceFlow.BuildingBlocks.Authentication` holds JWT
validation — the same forty lines that were in four services. Extracted in
Phase 7, once there were four copies to measure rather than two to guess from.

The line it draws: **mechanism is shared, policy is not.** How to validate a
token lives here; which roles a service cares about stays in that service.
No DTOs, no entities, nothing that crosses a service boundary.

## Tests

```bash
dotnet test
```

46 domain unit tests — no database, no mocks, no web host.
