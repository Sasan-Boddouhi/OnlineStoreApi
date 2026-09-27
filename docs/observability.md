# Observability

This document describes how OnlineStoreApi uses OpenTelemetry to produce
traces, metrics, and correlated logs.

The goal is to make production behavior observable without coupling the
application to any specific backend.

## 1. Overview

Three signals are emitted:

| Signal | Transport | Local Backend | Production Backend |
|--------|-----------|---------------|-------------------|
| Traces | OTLP/gRPC | Jaeger | Jaeger |
| Metrics | OTLP/gRPC | Prometheus + Grafana | *(not deployed)* |
| Logs | Serilog (Console) | Console + TraceId | Console + TraceId |

All signals flow through a single OpenTelemetry Collector instance,
which acts as an intermediary between the application and backends.

```text
                     ┌────────────────────┐
                     │   OnlineStoreApi   │
                     │   (OTel SDK)       │
                     └─────────┬──────────┘
                               │ OTLP/gRPC :4317
                               ▼
                     ┌────────────────────┐
                     │   OTel Collector   │
                     └─────────┬──────────┘
                               │
               ┌───────────────┴───────────────┐
               ▼                               ▼
         Jaeger (traces)               Prometheus (metrics)
                                              │
                                              ▼
                                           Grafana
```

Metrics pipeline is only enabled in Local development. Production only
runs traces + logs.

## 2. Traces

### 2.1 Automatic Instrumentation

Enabled via `OpenTelemetryExtensions.AddOpenTelemetryServices`:

| Source | Instrumentation |
|--------|-----------------|
| ASP.NET Core | `AddAspNetCoreInstrumentation` — server spans (except `/health`) |
| HttpClient | `AddHttpClientInstrumentation` |
| EF Core | `AddEntityFrameworkCoreInstrumentation` — SQL queries as child spans |
| StackExchange.Redis | `AddRedisInstrumentation` — Redis commands as child spans |
| Runtime | GC, thread pool, and other runtime metrics |

### 2.2 Custom ActivitySources

| ActivitySource | Owner class | Operations |
|----------------|-------------|------------|
| `OnlineStore.Auth` | `AuthService` | `RegisterUser`, `Login`, `RefreshToken`, `LogoutSession` |
| `OnlineStore.Services` | `ProductService` | `Product.GetById`, `Product.GetByQuery`, `Product.Create` |
| `OnlineStore.Cache` | `RedisCacheService` | `Cache.Get`, `Cache.Set`, `Cache.Remove` |

These sources must be registered via `AddSource()` in
`OpenTelemetryExtensions.AddOpenTelemetryServices`.

### 2.3 Span Tagging — PII Policy

**No PII is added to span tags.** This includes:

- Phone numbers
- Email addresses
- First and last names
- Passwords
- JWT or refresh token values

Safe identifiers that may be tagged:

- `user.id`, `session.id`, `product.id`
- `auth.failed_attempts`
- `cache.key`, `cache.hit`, `cache.type`
- `products.count`, `page.number`, `page.size`

The same policy applies to log messages: PII is never included in
structured log output.

## 3. Metrics

### 3.1 Custom Meters

Declared in `Application/Diagnostics/OnlineStoreMetrics.cs`:

**OnlineStore.Auth**

| Metric | Type | Tags |
|--------|------|------|
| `auth.login.attempts` | Counter | — |
| `auth.login.success` | Counter | — |
| `auth.login.failure` | Counter | `reason` |
| `auth.refresh.reuse_detected` | Counter | — |
| `auth.register.success` | Counter | — |

Failure reasons: `user_not_found`, `invalid_password`, `account_locked`.

**OnlineStore.Cache**

| Metric | Type | Tags |
|--------|------|------|
| `cache.hit` | Counter | `cache.type` |
| `cache.miss` | Counter | `cache.type` |
| `cache.operation.duration` | Histogram (ms) | `operation`, `cache.type` |

### 3.2 Prometheus Export

Collector's `prometheus` exporter (Local only) exposes metrics on port
`8889` with namespace `onlinestore`:

| Metric name | Prometheus name |
|-------------|-----------------|
| `auth.login.attempts` | `onlinestore_auth_login_attempts_total` |
| `auth.login.failure` | `onlinestore_auth_login_failure_failures_total` |
| `cache.hit` | `onlinestore_cache_hit_hits_total` |
| `cache.operation.duration` | `onlinestore_cache_operation_duration_milliseconds` |

## 4. Logs

### 4.1 Trace Correlation

`Serilog.Enrichers.Span` enriches every log entry with `TraceId` and
`SpanId` from `Activity.Current`. Output template:

```text
[14:23:15 INF] [TraceId=4bf92f35... SpanId=00f067aa...] {SourceContext}: {Message}
```

### 4.2 Correlation with Jaeger

Given a `TraceId` from any log entry, find the matching trace in Jaeger:

1. Open Jaeger UI.
2. Search by **Trace ID**.
3. Paste the `TraceId`.

No OTLP log backend is required for this correlation.

### 4.3 Not Implemented

- OTLP log export (no Loki/Elasticsearch)
- Log aggregation across containers

## 5. Configuration

### 5.1 OtelOptions

Bound from the `OpenTelemetry` configuration section in `appsettings.json`
and overridable by environment variables in Docker Compose.

| Property | Default | Purpose |
|----------|---------|---------|
| `Enabled` | `true` | Master kill switch |
| `ServiceName` | `OnlineStoreApi` | Reported service name |
| `ServiceVersion` | `1.0.0` | Reported version |
| `Endpoint` | `http://otel-collector:4317` | OTLP endpoint |
| `TraceSamplingRatio` | `1.0` | Head sampling ratio (0.0 - 1.0) |
| `IncludeEfCore` | `true` | Enable EF Core spans |
| `IncludeRedis` | `true` | Enable Redis spans |
| `ExportTimeoutMs` | `5000` | OTLP export timeout (ms) |
| `MaxBatchSize` | `2048` | Batch processor size |

### 5.2 Environment Overrides

Docker Compose overrides these values:

**Local** (`docker-compose.yml`):

```yaml
OpenTelemetry__Enabled: "true"
OpenTelemetry__Endpoint: "http://otel-collector:4317"
OpenTelemetry__TraceSamplingRatio: "1.0"
```

**Production** (`docker-compose.prod.yml`):

```yaml
OpenTelemetry__Enabled: "${OTEL_ENABLED:-true}"
OpenTelemetry__Endpoint: "http://otel-collector:4317"
OpenTelemetry__TraceSamplingRatio: "0.1"
```

### 5.3 Kill Switch

Disable OpenTelemetry entirely via `.env` on the VM:

```bash
echo "OTEL_ENABLED=false" >> ~/onlinestore-prod/.env
docker compose -f docker-compose.prod.yml up -d --force-recreate api
```

The API continues serving requests. Re-enable by removing the line.

The `Testing` environment is auto-disabled by the SDK regardless of
configuration (`!environment.IsEnvironment("Testing")` in
`AddOpenTelemetryServices`).

## 6. Deployment Topology

### 6.1 Local Development

`docker-compose.yml` runs:

| Container | Port | Purpose |
|-----------|------|---------|
| `onlinestore-api` | 5000 | API |
| `onlinestore-sqlserver` | 1433 | Database |
| `onlinestore-redis` | 6379 | Cache |
| `onlinestore-otel-collector` | 4317, 4318 | OTLP receiver |
| `onlinestore-jaeger` | 16686 | Trace UI |
| `onlinestore-prometheus` | 9090 | Metrics store |
| `onlinestore-grafana` | 3000 | Visualization |

### 6.2 Production VM

`docker-compose.prod.yml` runs a reduced set:

| Container | mem_limit | Purpose |
|-----------|-----------|---------|
| `onlinestore-api` | 512m | API |
| `onlinestore-sqlserver` | 1g | Database |
| `onlinestore-redis` | 128m | Cache |
| `onlinestore-otel-collector` | 256m | OTLP receiver |
| `onlinestore-jaeger` | 256m | Trace UI |

Prometheus and Grafana are intentionally omitted from production due to
VM resource constraints (3 GB RAM / 2 vCPU / ~10 GB disk).

Jaeger UI is bound to `127.0.0.1:16686` (VM-local only).

### 6.3 Sampling

| Environment | Sampling ratio |
|-------------|---------------|
| Development | 1.0 (100%) |
| Testing | disabled |
| Production | 0.1 (10%) |

## 7. Verification

### 7.1 Local — Trace in Jaeger

1. `docker compose up -d`
2. `curl http://localhost:5000/api/products`
3. Open `http://localhost:16686`
4. Search: Service = `OnlineStoreApi`, Operation = `GET api/Products`

Expected span tree:

```text
GET api/Products                            [Microsoft.AspNetCore]
  └── Product.GetByQuery                    [OnlineStore.Services]
        └── SELECT ... FROM Products        [Microsoft.EntityFrameworkCore]
```

### 7.2 Local — Metric in Prometheus

1. `http://localhost:9090`
2. Query: `onlinestore_auth_login_attempts_total`
3. Integer value reflecting total login attempts

### 7.3 Local — Grafana Dashboard

1. `http://localhost:3000` → `admin / admin`
2. Dashboard: **OnlineStore Overview**
3. Panels: Login Attempts, Success, Failures, Registrations, Cache Hits/Misses, Cache Duration

### 7.4 Local — Log Correlation

1. `docker logs onlinestore-api --tail 20`
2. Copy a `TraceId`
3. Search in Jaeger by Trace ID

### 7.5 Production VM

```bash
# Service list
curl -s http://127.0.0.1:16686/api/services

# Recent traces
curl -s "http://127.0.0.1:16686/api/traces?service=OnlineStoreApi&limit=5"
```

## 8. Operational Notes

### 8.1 OTel is Not on the Critical Path

The API does not depend on Collector availability. If the Collector
becomes unavailable:

- Traces are buffered in the batch processor.
- After the queue fills, spans are dropped.
- The API continues serving requests unaffected.

### 8.2 Collector Failure

Restart it independently:

```bash
docker compose -f docker-compose.prod.yml restart otel-collector
```

No API restart required.

### 8.3 Disabling Observability

See §5.3 (kill switch).

### 8.4 SQL Server Image Pinning

`docker-compose.prod.yml` pins SQL Server to
`mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`.

The `2022-latest` tag must not be used in production — it has exhibited
permission-related startup failures on non-root execution.

## 9. Non-Goals

Explicitly not part of the current implementation:

- OTLP log export (no Loki or Elasticsearch)
- Distributed tracing across multiple services
- Trace-based alerting
- SLO / SLI definitions
- Continuous profiling
- Chaos engineering
- Prometheus in Production (only Local)
- Grafana in Production (only Local)

## 10. Related Documentation

- `docs/architecture.md` — application layers and design patterns
- `docs/authentication.md` — authentication and session management
- `docs/deployment.md` — Docker and Production VM setup
- `docs/ci-cd.md` — GitHub Actions pipeline and rollback
