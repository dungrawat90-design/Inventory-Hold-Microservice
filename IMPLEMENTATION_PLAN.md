# Inventory Hold Microservice: implementation plan

Separate project. It follows the 10X standards (.NET, security, performance, logging, testing, API docs).
Approved decisions: compensating rollback for multi-SKU holds, xUnit + FluentAssertions, API-key auth,
hold lifetime default 15 min (configurable per request, max 60).

## Endpoints
- `POST /api/holds`: create a hold for one or more SKUs (all or nothing).
- `GET /api/holds/{holdId}`: read a hold.
- `DELETE /api/holds/{holdId}`: release a hold (idempotent).
- `GET /api/inventory`: paged stock with available, held and total.

## Layout
- `src/InventoryHold.Domain`: Hold, InventoryItem, domain errors.
- `src/InventoryHold.Application`: use cases (`HoldService`), ports, DTOs, FluentValidation, event contracts.
- `src/InventoryHold.Infrastructure`: MongoDB, Redis cache, MassTransit/RabbitMQ publisher, expiry worker, seeder.
- `src/InventoryHold.Api`: thin minimal-API endpoints, API-key auth, ProblemDetails, rate limiting, CORS, OpenAPI.
- `tests/InventoryHold.Tests`: unit tests (in-memory fakes, fake clock).
- `tests/InventoryHold.IntegrationTests`: Mongo atomicity tests with Testcontainers (need Docker).
- `web/`: React + TypeScript + Vite.
- `docker-compose.yml`, `src/InventoryHold.Api/Dockerfile`, `web/Dockerfile`.

## Core design
1. Inventory document: `available` and `held`. A hold moves quantity from `available` to `held`.
2. Atomic hold: conditional `FindOneAndUpdate` with filter `available >= qty`, so stock never goes negative
   and concurrent requests cannot oversell.
3. Multi-SKU: SKUs are reserved in sorted order; a failure restores the ones already reserved.
4. Idempotent release and expiry: the hold moves `Active` to `Released` or `Expired` with a conditional
   update. Only the caller that wins the transition restores stock, so stock is restored exactly once.
5. Expiry: a `BackgroundService` on a `PeriodicTimer` expires due holds using `TimeProvider`.
6. Redis: short-TTL cache of `GET /api/inventory` with version-key invalidation after every stock change.
7. RabbitMQ (MassTransit): `HoldCreated`, `HoldReleased`, `HoldExpired`, with retry and a fault queue; a
   logging consumer demonstrates consumption.

## Known limitations (documented, not hidden)
- Single-node Mongo has no transactions. A process crash between reserving stock and inserting the hold
  document can leave stock held with no hold. A replica set with a transaction would close this window.
- Events are published after the state change without a transactional outbox (a Mongo outbox needs a
  replica set). Delivery is at-least-once only while the process stays up.

## Verification
Built and unit-tested with the .NET 10 SDK and Node. Docker, MongoDB, Redis and RabbitMQ were not
available when this was built, so the Docker files and the integration tests are written but not run.
