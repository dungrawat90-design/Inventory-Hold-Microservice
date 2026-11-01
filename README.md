<<<<<<< HEAD
# Inventory-Hold-Microservice
An online shop must not sell the same last item twice. While a customer is paying, the shop wants to set the items aside for a short time. That is a hold.
=======
# Inventory Hold Microservice

Reserves stock for a short time while a customer completes checkout. A hold moves quantity from **available**
to **held**; it ends when the caller releases it or when it expires, and the stock goes back exactly once.

Stack: .NET 10 API, MongoDB, Redis, RabbitMQ (MassTransit), React + TypeScript + Vite. Everything runs in Docker.

## Run it (architect's machine)

**Prerequisite: Docker Desktop, installed and running.** Nothing else is needed to run the application: not .NET,
not Node, not MongoDB, Redis or RabbitMQ.

1. Install Docker Desktop and start it.
2. Extract or clone this project.
3. Open a terminal in the project root (the folder that contains `docker-compose.yml`).
4. Run:

   ```bash
   docker compose up --build
   ```

5. When the log shows the `web` container started (first run takes a few minutes while images download and
   build), open **http://localhost:8081**.

No `.env` file and no connection strings to set: every setting has a development default.

| What | URL | Notes |
|---|---|---|
| Web UI | http://localhost:8081 | Stock table, create hold, release hold, countdown |
| API | http://localhost:5080 | Needs header `X-Api-Key: dev-only-api-key-0123456789` |
| OpenAPI document | http://localhost:5080/openapi/v1.json | Contract also in [Stoplight/inventory-hold.openapi.yaml](Stoplight/inventory-hold.openapi.yaml) |
| Health | http://localhost:5080/health/live, `/health/ready` | `ready` checks MongoDB, Redis and RabbitMQ |
| RabbitMQ management | http://localhost:15672 | user `hold`, password `dev-only-rabbit-password` |
| MongoDB | `mongodb://localhost:27017` | Loopback only, for the integration tests |

Try the API directly:

```bash
curl -s http://localhost:5080/api/inventory -H "X-Api-Key: dev-only-api-key-0123456789"

curl -s -X POST http://localhost:5080/api/holds \
  -H "X-Api-Key: dev-only-api-key-0123456789" -H "Content-Type: application/json" \
  -d '{"items":[{"sku":"DEMO-1001","quantity":2}],"ttlMinutes":15}'
```

Stop with `Ctrl+C`. `docker compose down` removes the containers and keeps the data; `docker compose down -v`
also deletes the MongoDB and RabbitMQ volumes for a clean start.

### What happens at start-up

1. MongoDB, Redis and RabbitMQ start. Each has a health check.
2. The API waits until all three are **healthy**, then starts. It creates its index and seeds five demo products
   (`DEMO-1001` 25, `DEMO-1002` 10, `DEMO-1003` 5, `DEMO-1004` 100, `DEMO-1005` 0). Seeding only inserts products that
   do not exist yet, so starting again never resets or duplicates stock. If MongoDB is slow, the API retries for
   about a minute instead of exiting.
3. The web container waits for the API's `/health/ready` to answer 200, then serves the UI.

Containers reach each other by compose service name (`mongo`, `redis`, `rabbitmq`, `api`); `localhost` is never used
between containers. The browser only ever talks to the web container's own address: nginx forwards `/api/...` to
the API and adds the API key there, so no internal host name or key reaches the browser.

### Configuration

All optional, via environment variables or a `.env` file (see [.env.example](.env.example)):
`API_KEY`, `RABBITMQ_USER`, `RABBITMQ_PASSWORD`, `WEB_PORT`, `API_PORT`, `RABBITMQ_UI_PORT`, `MONGO_PORT`,
`SEED_DEMO_DATA`, `HOLD_DEFAULT_TTL_MINUTES`, `HOLD_EXPIRY_INTERVAL_SECONDS`.

The default API key and RabbitMQ password are for local demonstration only. Change them for any shared environment.

## Verify it

Run these from the project root **after** `docker compose up --build` is healthy. They need the .NET 10 SDK and
Node 22 on the machine that runs them (not needed just to run the app).

```bash
# 1. API + MongoDB + Redis + RabbitMQ together, against the running stack (about 2 minutes, one test waits for expiry)
dotnet test tests/InventoryHold.IntegrationTests

#    faster, without the expiry wait:
dotnet test tests/InventoryHold.IntegrationTests --filter "Category!=Slow"

# 2. Browser tests against http://localhost:8081
cd web
npm ci
npx playwright install chromium
npm run e2e
```

Tests that need no running services:

```bash
dotnet test tests/InventoryHold.Tests          # 58 unit and HTTP tests
cd web && npm test                             # 25 component and client tests
```

What the integration tests prove against the real stack: readiness of all three dependencies, five seeded products,
create/read/release through MongoDB, the Redis cache staying current after every change, 60 simultaneous requests
for 10 units giving exactly 10 holds, all-or-nothing multi-SKU holds, releasing twice restoring once, events
reaching RabbitMQ and being consumed, and a hold expiring by itself and returning its stock.

### Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `port is already allocated` | A host port is taken. Set `WEB_PORT`, `API_PORT`, `RABBITMQ_UI_PORT` or `MONGO_PORT` (for example `WEB_PORT=9000 docker compose up --build`). |
| `docker: command not found` or "cannot connect to the Docker daemon" | Docker Desktop is not installed or not running. Start it and retry. |
| First start is slow | Images are downloading and the API and UI are building. Later starts take seconds. |
| `api` stays "starting" | Look at `docker compose logs api`. RabbitMQ takes up to about a minute to become healthy; the API waits for it. |
| Containers restart in a loop | Run `docker compose logs <service>`. If a data volume came from an older version, `docker compose down -v` and start again. |
| UI loads but shows an error | Check `docker compose ps`: `api` must be healthy. The error shows a trace id; find it with `docker compose logs api`. |
| Integration tests cannot connect | The stack is not up, or you changed a port. Set `STACK_API_URL`, `STACK_API_KEY`, `STACK_MONGO_CONNECTION`, `MONGO_TEST_CONNECTION`, `RABBITMQ_UI_URL` to match. |

## API

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/holds` | Create a hold for one or more SKUs. All or nothing. |
| GET | `/api/holds/{holdId}` | Read a hold. |
| DELETE | `/api/holds/{holdId}` | Release a hold. Repeating the call is safe. |
| GET | `/api/inventory` | Paged stock: available, held, total. `take` defaults to 50, maximum 200. |

Every request needs `X-Api-Key`. Errors are RFC 7807 ProblemDetails with a `traceId`. `/health/*` is anonymous.

## How it works

- **Atomic hold.** Each SKU is reserved with one conditional `FindOneAndUpdate` (`available >= quantity`, then
  `$inc`). MongoDB applies the check and the change together, so concurrent requests cannot oversell and stock cannot
  go negative.
- **Several SKUs.** SKUs are reserved in sorted order. If one fails, the ones already reserved are put back. This
  needs no MongoDB transaction, so **no replica set is configured or required**.
- **Release and expiry.** The hold moves from `Active` to `Released` or `Expired` with a conditional update. Only the
  caller that wins that update restores stock, so a manual release racing the expiry worker, or two releases,
  restore once.
- **Expiry.** A background worker wakes every 15 seconds, expires due holds in batches and returns their stock.
- **Redis.** `GET /api/inventory` pages are cached for 30 seconds. Every change bumps a generation counter that is
  part of the cache key, so one `INCR` retires all pages, and a page loaded before a change can never be served
  after it. If Redis is down the API reads from MongoDB.
- **RabbitMQ.** `HoldCreated`, `HoldReleased` and `HoldExpired` are published with MassTransit. The bus retries
  failed deliveries (3 times, 10 seconds apart); messages that still fail go to the endpoint's `_error` queue. A
  reference consumer logs each event. A broker outage never fails a hold request.
- **Frontend.** One page: stock table, create-hold form, and the holds made in this browser with a countdown and
  a release button. Loading, empty and error states, trace id on failures, phone-width and dark mode.

## Known limitations

- **Crash window.** Without a transaction, a process crash after reserving stock but before saving the hold leaves
  that stock held with no hold to expire it. A MongoDB replica set with a transaction would close this.
- **No transactional outbox.** Events are published after the state change. If the process dies in between, or the
  broker is down for the 5-second publish timeout, that event is lost.
- **Expiry lag.** A hold shows `Active` until the worker expires it (up to 15 seconds after its expiry time).
- **One shared API key**, no per-user identities. Anyone who can reach the web UI can use the API through it.
- **MongoDB has no authentication** in the compose file. It is published on loopback only, for the tests.

## Running from Visual Studio or `dotnet run`

`src/InventoryHold.Api/appsettings.json` holds **development placeholder** settings: API key `dev-only-api-key-0123456789`,
MongoDB `mongodb://localhost:27017`, Redis `localhost:6379`, RabbitMQ `localhost` with `guest` / `guest`. Replace them
with real values (or override with environment variables such as `RabbitMq__Password`) before any shared use. The
Docker setup overrides all of them through compose, so the file does not affect containers.

Two launch profiles are provided:
- **InMemory demo** (the default): runs with no MongoDB, Redis or RabbitMQ at all.
- **Real infrastructure (localhost)**: expects those three services on the addresses above.

## No Docker? Quick in-memory demo

With `Demo__InMemory=true` the API keeps data in process memory and logs events instead of using MongoDB, Redis or
RabbitMQ. It is for a quick look only and proves nothing about those services. Needs the .NET 10 SDK and Node 22.

```bash
export Auth__ApiKey=demo-key-0123456789abcdef Demo__InMemory=true Holds__ExpiryIntervalSeconds=5
dotnet run --project src/InventoryHold.Api --urls http://localhost:5080
cd web && API_KEY=demo-key-0123456789abcdef npm run dev      # http://localhost:5173
```

## Project layout

`src/InventoryHold.Domain` (rules) · `.Application` (use cases, validation) · `.Infrastructure` (MongoDB, Redis,
RabbitMQ, expiry worker) · `.Api` (endpoints, auth, errors) · `web/` (React UI, nginx) · `tests/` ·
[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

## Verification status of this submission

| Check | Status |
|---|---|
| `dotnet build -warnaserror`, `dotnet format --verify-no-changes` | Run, pass |
| 58 unit and HTTP tests, 25 frontend tests, frontend typecheck and build | Run, pass |
| Docker image builds, `docker compose up`, container health checks | **Not run**: Docker was not available where this was built |
| `tests/InventoryHold.IntegrationTests` against the running stack | **Not run**, same reason |
| Playwright E2E | **Not run**, same reason |
| Real MongoDB, Redis and RabbitMQ behaviour | **Not run**; written to the drivers' documented behaviour only |
| Compose file, Dockerfile inputs, nginx template | Checked statically only (structure, dependencies, defaults, no localhost between containers) |

The three commands in "Verify it" are the first thing to run on the architect's machine. Expect that a first
real run may turn up small configuration problems (a health check, a Dockerfile detail) that could not be seen
without Docker.
>>>>>>> 17ebc8a (Initial commit - Inventory Hold Microservice)
