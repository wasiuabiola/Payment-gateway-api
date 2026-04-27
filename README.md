# Payment Gateway API

A production-grade multi-provider payment gateway built with **ASP.NET Core 8 / C# 12**, demonstrating the architecture patterns used in fintech systems: clean separation of concerns, idempotency, webhook retry with exponential backoff, Redis caching, and resilient HTTP clients.

---

## Architecture

```
PaymentGateway.sln
├── src/
│   ├── PaymentGateway.API              # Web API — controllers, middleware, DTOs
│   ├── PaymentGateway.Core             # Domain — models, interfaces, exceptions (zero external deps)
│   └── PaymentGateway.Infrastructure   # External concerns — providers, EF Core, Redis
└── tests/
    └── PaymentGateway.Tests            # xUnit unit tests with Moq + FluentAssertions
```

### Key Design Decisions

| Decision | Rationale |
|---|---|
| **Clean Architecture** | Domain layer has zero infrastructure dependencies — swap SQL Server for Postgres or Paystack for Stripe without touching business logic |
| **Provider Pattern** | `IPaymentProvider` abstraction means adding a new payment provider is one file, zero changes elsewhere |
| **Idempotency via Redis** | Write endpoints require an `Idempotency-Key` header; duplicate requests within 24 h return the cached response without re-processing the payment |
| **Centralized exception handling** | `ExceptionHandlingMiddleware` maps domain exceptions to HTTP status codes in one place — controllers stay clean |
| **Polly resilience** | All provider HTTP clients use `AddStandardResilienceHandler()` — automatic retry with exponential backoff + circuit breaker, preventing cascading failures |
| **Rate limiting** | Built-in ASP.NET Core rate limiter: 100 requests/minute per client — protects against abuse on payment endpoints |

---

## Tech Stack

| Concern | Technology |
|---|---|
| Framework | ASP.NET Core 8 |
| Language | C# 12 |
| ORM | Entity Framework Core 8 (SQL Server) |
| Cache / Idempotency | Redis (StackExchange.Redis) |
| Auth | JWT Bearer Tokens |
| Resilience | Polly via `Microsoft.Extensions.Http.Resilience` |
| Logging | Serilog (structured, JSON-ready) |
| Testing | xUnit + Moq + FluentAssertions |

---

## Getting Started

### Prerequisites

- .NET 8 SDK
- SQL Server (or Docker)
- Redis (or Docker)

### 1 — Start dependencies with Docker

```bash
docker run -d -p 6379:6379 redis
docker run -d -p 1433:1433 -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrong@Passw0rd" mcr.microsoft.com/mssql/server:2022-latest
```

Update `appsettings.json` if using the Docker SQL Server:

```json
"DefaultConnection": "Server=localhost,1433;Database=PaymentGateway;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;"
```

### 2 — Apply database migrations

```bash
dotnet ef database update --project src/PaymentGateway.Infrastructure --startup-project src/PaymentGateway.API
```

### 3 — Run the API

```bash
dotnet run --project src/PaymentGateway.API
```

Swagger UI: `https://localhost:63349/swagger`  
Health check: `https://localhost:63349/health`

---

## Authentication

The API uses JWT bearer tokens. Obtain a token first, then include it in every subsequent request.

### Get a token

```http
POST /api/v1/auth/token
Content-Type: application/json

{
  "apiKey": "dev-api-key-replace-in-production"
}
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2026-04-27T18:00:00Z"
}
```

Paste the token into Swagger's **Authorize** button as `Bearer <token>`.

---

## API Reference

### Initiate Payment

```http
POST /api/v1/payments
Authorization: Bearer {token}
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
Content-Type: application/json

{
  "reference": "TXN-2026-001",
  "amountInKobo": 50000,
  "currency": "NGN",
  "provider": "Paystack",
  "customer": {
    "email": "customer@example.com",
    "name": "John Doe",
    "phone": "+2348012345678"
  },
  "metadata": {
    "orderId": "ORD-001",
    "channel": "web"
  }
}
```

**Response `200 OK`:**
```json
{
  "transactionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Pending",
  "authorizationUrl": "https://checkout.paystack.com/abc123",
  "reference": "TXN-2026-001",
  "provider": "Paystack",
  "createdAt": "2026-04-27T10:30:00Z"
}
```

**Error responses:** `400` (validation failure), `409` (duplicate reference), `502` (provider error)

---

### Verify Payment

```http
GET /api/v1/payments/{reference}/verify
Authorization: Bearer {token}
```

Results are cached in Redis for 5 minutes after a successful confirmation.

---

### Refund

```http
POST /api/v1/payments/{transactionId}/refund
Authorization: Bearer {token}
Idempotency-Key: {uuid}
Content-Type: application/json

{
  "amount": 50000,
  "reason": "Customer request"
}
```

Only `Successful` transactions can be refunded. Attempting to refund a `Pending` or `Failed` transaction returns `400`.

---

### Webhooks

```http
POST /api/v1/webhooks/paystack
X-Paystack-Signature: {hmac-sha512-of-payload}

POST /api/v1/webhooks/interswitch
X-Interswitch-Signature: {hmac-sha256-of-payload}
```

Each webhook verifies the provider's HMAC signature before processing. Invalid signatures return `400` immediately.

---

## Idempotency

All write endpoints (`POST /payments`, `POST /refund`) require an `Idempotency-Key` header containing a UUID v4. Sending the same key twice within 24 hours returns the original response with an `Idempotency-Replayed: true` header — the payment is never processed twice.

```
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
```

---

## Webhook Retry Strategy

Failed webhook deliveries are retried with exponential backoff and tracked in the database:

| Attempt | Delay after failure |
|---|---|
| 1 | 30 seconds |
| 2 | 5 minutes |
| 3 | 30 minutes |
| 4 | 2 hours |
| 5 | → Dead Letter |

After 5 failed attempts the delivery is marked `DeadLetter` for manual review. Call `IWebhookService.ProcessPendingAsync()` from a background job (e.g. Hangfire, hosted service) to drive retry execution.

---

## Running Tests

```bash
dotnet test tests/PaymentGateway.Tests
```

Tests cover `PaymentService` business logic directly — not mocks testing mocks:

- Initiate: new reference saves transaction, duplicate throws `DuplicateTransactionException`, provider failure skips DB write
- Verify: cache hit skips repository, successful result updates transaction + caches, unknown reference throws `TransactionNotFoundException`
- Refund: successful transaction processes and marks as refunded, pending transaction throws `InvalidTransactionStateException`
- Webhook: invalid signature throws, valid signature processes without error
- Domain model: `Transaction` state transitions and event recording

---

## Configuration

| Key | Description |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |
| `ConnectionStrings:Redis` | Redis connection string |
| `Jwt:SecretKey` | HS256 signing key (min 32 chars) |
| `Auth:ApiKey` | API key used to obtain JWT tokens |
| `Providers:Paystack:SecretKey` | Paystack secret key (`sk_live_...`) |
| `Providers:Interswitch:ClientId` | Interswitch client ID |
| `Providers:Interswitch:CallbackUrl` | Payment redirect URL after checkout |

For production, inject these as environment variables or use Azure Key Vault / AWS Secrets Manager. Never commit real secrets to source control.

---

## What I Would Build Next

This project intentionally represents a slice of a production-grade system. Below is the roadmap I would execute given more time — written in priority order, not wishlist order.

### 1. Transactional Outbox Pattern for Webhook Reliability

The current webhook delivery is best-effort: if the process restarts after a payment is confirmed but before `EnqueueAsync` is called, the delivery is silently lost. The fix is an **outbox table** written in the same database transaction as the status update, and a separate poller that reads from it. This guarantees at-least-once delivery with zero risk of silent data loss — a correctness requirement in fintech, not an optimisation.

### 2. Dead-Letter Queue Admin API

Deliveries that exhaust all retry attempts are currently marked `DeadLetter` and left in the database. An ops team needs to action them without direct SQL access. I would expose:

- `GET /api/v1/admin/webhook-deliveries?status=DeadLetter` — paginated list with payload previews
- `POST /api/v1/admin/webhook-deliveries/{id}/retry` — re-enqueue a single delivery
- `DELETE /api/v1/admin/webhook-deliveries/{id}` — acknowledge and discard

### 3. Background Job Host for Retry Execution

`IWebhookService.ProcessPendingAsync()` must currently be driven by something external. In production this would be a `BackgroundService` (or Hangfire recurring job) that runs on a configurable interval, claims a batch of due deliveries under a distributed lock (Redis `SET NX`), and processes them. Without a distributed lock, multiple horizontally-scaled instances would race to deliver the same webhook.

### 4. OpenTelemetry Distributed Tracing

Every inbound request should emit a trace span. Payment initiation spans should include child spans for the provider HTTP call and the database write, linked by a shared `TraceId`. When a webhook arrives days later, correlating it back to the original transaction without a trace is guesswork. I would wire `AddOpenTelemetry()` with OTLP export to Grafana Tempo or Jaeger, and attach `payment.reference`, `payment.provider`, and `payment.status` as semantic attributes on each span.

### 5. Per-Merchant Rate Limiting and Configuration

The current rate limiter applies a single global policy (100 req/min) to all authenticated clients. In a real multi-tenant gateway each merchant has a different plan tier. I would replace the fixed-window global limiter with a per-`ClientId` sliding-window policy backed by Redis, and store merchant-specific limits in a `MerchantConfiguration` table. Provider credentials (Paystack secret key, Interswitch client ID) would move from `appsettings` into that table, enabling runtime provider switching without a redeploy.

### 6. Fallback Provider Routing

If Paystack returns a 5xx or the Polly circuit breaker trips open, the payment fails with no recovery path. A routing layer could automatically retry on the next configured provider for the same currency (e.g. Paystack → Interswitch for NGN). The `InitiatePaymentResult` already carries enough information to detect a provider-level failure; the routing logic would live in a `FallbackPaymentService` decorator wrapping `IPaymentService`.

### 7. Idempotency Key Expiry Visibility

Redis TTL-based expiry is invisible to callers: a key expires and the next request re-processes silently. I would add a `X-Idempotency-Expires-At` response header and a `GET /api/v1/idempotency/{key}` endpoint so integrators can inspect the state of a key without triggering side effects.

### 8. docker-compose and GitHub Actions CI

Currently developers must run SQL Server and Redis manually. I would add a `docker-compose.yml` that brings up the full stack (API + SQL Server + Redis) with a single command, and a `.github/workflows/ci.yml` that runs `dotnet test` on every PR, enforces test coverage thresholds, and publishes a Docker image on merge to `main`.

### 9. PCI DSS Compliance Hardening

Production payment APIs are subject to PCI DSS requirements. Concretely: TLS 1.2+ enforcement, no card data stored anywhere in this service (we delegate to provider-hosted checkout pages, which is the right architecture), structured audit logs for every state transition (already partially in place via `TransactionEvent`), and secret rotation without downtime via versioned keys in Azure Key Vault.

---

## Author

**Wasiu Abiola** — Backend Engineer  
[wasiuabiola.dev](https://wasiuabiola.dev) · [LinkedIn](https://linkedin.com/in/wasiu-abiola) · [GitHub](https://github.com/wasiuabiola)
