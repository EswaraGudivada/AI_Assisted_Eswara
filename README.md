# URL Shortener (.NET 10)

A small URL shortener: create a short code for a long URL, look the code up to get the URL back, and see how often it
was used. Built with ASP.NET Core minimal APIs, EF Core and SQLite.

It follows the design of the reference repository
[madhan9491k-cmd/HomeExam-URLShortener](https://github.com/madhan9491k-cmd/HomeExam-URLShortener), and keeps the
extra material asked for by the original requirement (Docker, CI, engineering scenarios and an AI-usage log).

> **Lookups return the URL; they do not redirect.** `GET /{code}` answers `200 OK` with the original URL as a JSON
> string. This was chosen on purpose so the result is visible in Swagger and API clients. A browser opening a short
> link will show the URL rather than going there.

## Contents

1. [Run it](#1-run-it)
2. [Try it](#2-try-it)
3. [Run it on another machine](#3-run-it-on-another-machine)
4. [Test it](#4-test-it)
5. [How it is built](#5-how-it-is-built)
6. [API](#6-api)
7. [Database](#7-database)
8. [Security and privacy](#8-security-and-privacy)
9. [When things go wrong](#9-when-things-go-wrong)
10. [Settings](#10-settings)
11. [Decisions and trade-offs](#11-decisions-and-trade-offs)
12. [Limitations](#12-limitations)
13. [AI-assisted engineering](#13-ai-assisted-engineering)
14. [Repository layout](#14-repository-layout)

## 1. Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet run --project src/UrlShortener.Api
```

- The API listens on **http://localhost:5080**.
- Swagger UI is at **http://localhost:5080/swagger** (OpenAPI JSON at `/openapi/v1.json`).
- The SQLite file `urlshortener.db` is created next to the app, and the EF Core migrations are applied on start.

`dotnet run` uses the `Development` environment, which turns on Swagger and allows `localhost` targets so you can
shorten links to other local apps. Neither is on in other environments unless you ask for it (see [Settings](#10-settings)).

### With Docker

```bash
docker build -t urlshortener .
docker run --rm -p 8080:8080 -v urlshortener-data:/data urlshortener
```

The container listens on port 8080, keeps its database in the `/data` volume, and runs as a non-root user. It runs
in `Production` mode, so Swagger is off; add `-e OpenApi__Enabled=true` to turn it on. Set
`-e Shortener__BaseUrl=https://your.domain` so returned short URLs use your public address.

## 2. Try it

```bash
# Create a link (201). "alias" and "expiresAt" are optional.
curl -s -X POST http://localhost:5080/api/v1/links \
  -H "Content-Type: application/json" \
  -d '{"url":"https://learn.microsoft.com/dotnet","alias":"dotnet-docs"}'

# Look it up: 200 with the URL as a JSON string
curl -s http://localhost:5080/dotnet-docs
# "https://learn.microsoft.com/dotnet"

# Details and click statistics (default window: last 30 days)
curl -s http://localhost:5080/api/v1/links/dotnet-docs
curl -s "http://localhost:5080/api/v1/links/dotnet-docs/stats?from=2026-01-01T00:00:00Z"

# List links, newest first
curl -s "http://localhost:5080/api/v1/links?page=1&pageSize=20"

# Deactivate it (204). Lookups now return 410. Repeating the delete is safe.
curl -s -X DELETE http://localhost:5080/api/v1/links/dotnet-docs

# Health
curl -s http://localhost:5080/health
```

Click statistics are written in the background, so they can lag a lookup by a moment.

## 3. Run it on another machine

**A. From source.** Install the .NET 10 SDK, clone the repository and run `dotnet run --project src/UrlShortener.Api`.
To accept connections from other devices, bind to all interfaces:

```bash
ASPNETCORE_URLS=http://0.0.0.0:5080 Shortener__BaseUrl=http://<this-machine-ip>:5080 \
  dotnet run --project src/UrlShortener.Api
```

**B. With Docker.** Build and run the image as shown above. Only Docker is needed on the target machine.

**C. As a single self-contained build.** No .NET install is needed on the target:

```bash
dotnet publish src/UrlShortener.Api -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish
# Windows: -r win-x64, macOS (Apple silicon): -r osx-arm64
cd publish
ASPNETCORE_URLS=http://0.0.0.0:5080 ./UrlShortener.Api
```

Copy the `publish` folder to the other machine and start `UrlShortener.Api`. The database file is created in the
working directory unless `ConnectionStrings__Default` points elsewhere.

For a real deployment also set `Shortener__BaseUrl` to the public address, put the service behind HTTPS, and decide
whether migrations run at start (`Database__AutoMigrate`) or from your release pipeline:

```bash
dotnet tool restore
dotnet ef database update -p src/UrlShortener.Infrastructure -s src/UrlShortener.Infrastructure
```

## 4. Test it

```bash
dotnet test UrlShortener.slnx
```

196 tests: 153 unit tests and 43 integration tests.

- **Unit tests** cover the service rules with hand-written fakes and a fake clock: code generation and collisions,
  aliases, URL checks, private-network and denied-host rules, expiry, deduplication, negative caching, statistics
  windows, browser-family classification, the click queue and its health check, and architecture rules (Core must not
  depend on EF Core, ASP.NET Core, `Microsoft.Extensions` or the outer projects).
- **Integration tests** start the real API with `WebApplicationFactory` against a fresh SQLite file created through
  the migrations. They cover every endpoint, status codes and error bodies, correlation ids, rate limiting, health,
  Swagger gating and the background click pipeline.

The same checks run in CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)):

```bash
dotnet format UrlShortener.slnx --verify-no-changes
dotnet build UrlShortener.slnx -c Release          # warnings are errors
dotnet test UrlShortener.slnx -c Release
dotnet list UrlShortener.slnx package --vulnerable --include-transitive
docker build -t urlshortener .
```

## 5. How it is built

```
src/UrlShortener.Core            rules and abstractions only; no framework references
src/UrlShortener.Infrastructure  EF Core + SQLite, cache, click queue, health checks, metrics
src/UrlShortener.Api             HTTP endpoints, errors, rate limits, correlation ids, Swagger
```

Dependencies point inwards: Api → Infrastructure → Core. A test enforces this.

**Creating a link.** The URL is trimmed and parsed (absolute `http`/`https`, no credentials, at most 2048 characters),
then passed through the safety rules (private networks, denied hosts, the service's own host). A generated code is 7
characters from a cryptographically secure random source over `[A-Za-z0-9]`. The database's unique index decides
collisions; the service retries a few times and then answers `503`. Custom aliases are 4–32 characters of letters,
digits, `-` and `_`, and words such as `api`, `swagger` and `health` are reserved. Shortening the same URL again
without alias or expiry returns the existing link (`200`) instead of a new one.

**Looking up a code.** The format is checked before any I/O. The link comes from the in-memory cache or the database;
unknown codes are also cached briefly ("known missing") so repeated guesses do not reach the database. Expired or
deactivated links return `410`. On success a click event is put on a bounded in-memory queue and the URL is returned
immediately; a background worker writes clicks in batches and updates each link's counter. If the queue is full the
click is dropped and counted, rather than slowing the lookup.

**Statistics** are computed from stored click events for a `from`/`to` window (default: last 30 days, at most 366
days): total clicks, clicks in range, clicks per day (with zero days filled in) and top referring sites.

## 6. API

| Method | Path | Result |
|---|---|---|
| `POST` | `/api/v1/links` | `201` new link (`Location` header), `200` existing link for the same URL, `409` alias taken, `422` invalid input |
| `GET` | `/api/v1/links?page=&pageSize=` | `200` page of links, newest first (default 20, max 100) |
| `GET` | `/api/v1/links/{code}` | `200` link details (including deactivated links), `404` |
| `DELETE` | `/api/v1/links/{code}` | `204` (also when already deactivated), `404` unknown code |
| `GET` | `/api/v1/links/{code}/stats?from=&to=` | `200` statistics, `404`, `422` bad range |
| `GET` | `/{code}` | `200` with the URL as a JSON string, `404`, `410` expired or deactivated |
| `GET` | `/health` | `200` Healthy/Degraded, `503` Unhealthy, with a JSON breakdown |
| `GET` | `/swagger`, `/openapi/v1.json` | Swagger UI and OpenAPI document, when enabled |

Writes (create, delete, stats) and lookups have separate per-IP rate limits; over the limit you get `429` with a
`Retry-After` header.

**Create request**

```json
{ "url": "https://example.com/page", "alias": "my-link", "expiresAt": "2026-12-31T23:59:59Z" }
```

**Link response**

```json
{
  "code": "my-link",
  "shortUrl": "http://localhost:5080/my-link",
  "targetUrl": "https://example.com/page",
  "status": "active",
  "isCustom": true,
  "createdAt": "2026-10-12T22:00:00+00:00",
  "expiresAt": "2026-12-31T23:59:59+00:00",
  "totalClicks": 3,
  "lastAccessedAt": "2026-10-12T22:05:00+00:00"
}
```

`status` is `active`, `expired` or `deactivated`. The list endpoint wraps items with `page`, `pageSize`, `totalCount`,
`totalPages` and `hasNextPage`.

**Statistics response**

```json
{
  "code": "my-link",
  "totalClicks": 3,
  "lastAccessedAt": "2026-10-12T22:05:00+00:00",
  "from": "2026-09-12T22:06:00+00:00",
  "to": "2026-10-12T22:06:00+00:00",
  "clicksInRange": 3,
  "daily": [{ "date": "2026-10-12", "clicks": 3 }],
  "topReferrers": [{ "referrer": "news.example", "clicks": 2 }, { "referrer": "(direct)", "clicks": 1 }]
}
```

**Errors** use RFC 7807 Problem Details with a stable `code` and the request's `correlationId`:

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "Unprocessable Entity",
  "status": 422,
  "detail": "The target URL must point at a public host. Private, loopback, and link-local addresses are not allowed.",
  "code": "url_private_target",
  "correlationId": "0HN7..."
}
```

| Status | `code` |
|---|---|
| 400 | (body could not be read: malformed JSON or wrong types) |
| 404 | `link_not_found` |
| 409 | `alias_taken` |
| 410 | `link_expired`, `link_deactivated` |
| 422 | `url_invalid`, `url_private_target`, `url_host_denied`, `alias_invalid`, `alias_reserved`, `expiry_invalid`, `paging_invalid`, `range_inverted`, `range_too_wide` |
| 429 | `rate_limit_exceeded` |
| 503 | `code_generation_exhausted` |

Send an `X-Correlation-Id` header (up to 64 letters, digits, `-` or `_`) to have your own id used; otherwise one is
generated. It is returned on every response and added to every log line for the request.

## 7. Database

SQLite through EF Core, with migrations in
[`src/UrlShortener.Infrastructure/Persistence/Migrations`](src/UrlShortener.Infrastructure/Persistence/Migrations).

| Table | Columns | Indexes |
|---|---|---|
| `short_links` | `Id`, `Code`, `OriginalUrl`, `CreatedAt`, `ExpiresAt`, `IsActive`, `IsCustomAlias`, `ClickCount`, `LastAccessedAt` | unique `Code`; `OriginalUrl`; `CreatedAt` |
| `click_events` | `Id`, `Code`, `OccurredAt`, `RefererHost`, `UserAgentFamily` | (`Code`, `OccurredAt`) |

Timestamps are stored as sortable integers so SQLite can compare and order them. A database created by an earlier version of this
project (before migrations) is upgraded in place on start: the click columns are renamed, stored browser strings are
reduced to a browser family, and the initial migration is recorded as applied. Deleting a link only sets
`IsActive = false`; codes are never reused and history is kept.

To change the schema: edit the model, then

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/UrlShortener.Infrastructure -s src/UrlShortener.Infrastructure -o Persistence/Migrations
```

## 8. Security and privacy

- **No login or API key.** Anyone who can reach the service can create, list and deactivate links. Rate limits and
  target checks are the protection in this prototype; put it behind authentication before exposing it publicly.
- **Target checks.** Only `http`/`https`, no embedded credentials, maximum length. Outside Development, targets on
  `localhost`, `.local`/`.internal` names, loopback, private ranges (10/8, 172.16/12, 192.168/16), CGNAT (100.64/10),
  link-local including the cloud metadata address `169.254.169.254`, and private IPv6 ranges are refused. Host names
  are not resolved through DNS (the answer could change after the check, and this service never fetches targets).
  `Security:DeniedHosts` blocks hosts and their subdomains; the service's own host is always blocked.
- **Codes** come from a cryptographically secure generator, so they cannot be enumerated in order.
- **Rate limits** per client IP: writes use a token bucket (30/minute by default), lookups a fixed window (600/minute).
  Behind a proxy, configure forwarded headers so the real client IP is used.
- **Headers.** `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, HSTS outside
  Development. Lookups send `Cache-Control: no-store, no-cache, must-revalidate`.
- **Swagger/OpenAPI** are off outside Development unless `OpenApi__Enabled=true`.
- **Privacy.** Clicks store the time, the referring site's host (no path or query) and a browser family (`chrome`,
  `firefox`, `safari`, `edge`, `opera`, `bot`, `tool`, `other`). No IP addresses and no full user-agent strings.
- Configuration is validated at start-up; an invalid value stops the service instead of failing later.

## 9. When things go wrong

| Symptom | Likely cause | What to do |
|---|---|---|
| `/health` is `Degraded` | Click queue above 80% full; clicks may be dropped | Check database speed; raise `Analytics:QueueCapacity` or `BatchSize` |
| `/health` is `Unhealthy` (503) | Database unreachable | Check `ConnectionStrings:Default`, file permissions and disk space |
| Service stops at start-up with an options error | Invalid setting (e.g. bad `Shortener:BaseUrl`) | Fix the value named in the message |
| Many `429` responses | Rate limit reached, or all clients share a proxy IP | Raise `RateLimit:*` or configure forwarded headers |
| Statistics lower than expected | Clicks still queued, or dropped under overload | Wait a moment; watch `urlshortener.clicks.dropped` |
| New link returns `404` for a few seconds on another instance | That instance cached "known missing" | Expected with several instances; lower `Cache:MissingTtlSeconds` |
| `/swagger` returns `404` | Not Development and `OpenApi:Enabled` not set | Set `OpenApi__Enabled=true` |

**Metrics** are published on the .NET meter `UrlShortener` and can be read with
`dotnet-counters monitor -n UrlShortener.Api --counters UrlShortener` or any OpenTelemetry exporter:
`urlshortener.resolutions`, `urlshortener.links.created`, `urlshortener.cache.lookups`,
`urlshortener.clicks.enqueued`, `urlshortener.clicks.dropped`, `urlshortener.clicks.persisted`,
`urlshortener.clicks.queue_depth`.

## 10. Settings

Set in `appsettings.json` or as environment variables (`Section__Key`, e.g. `RateLimit__WritesPerMinutePerIp=60`).

| Setting | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=urlshortener.db` | SQLite database |
| `Database:AutoMigrate` | `true` | Apply migrations at start |
| `OpenApi:Enabled` | `true` in Development, otherwise `false` | Swagger UI and `/openapi/v1.json` |
| `Shortener:BaseUrl` | `http://localhost:5080` | Public address used in `shortUrl` |
| `Shortener:CodeLength` | `7` | Length of generated codes |
| `Shortener:MaxGenerationAttempts` | `3` | Retries on code collision before `503` |
| `Shortener:DefaultPageSize` / `MaxPageSize` | `20` / `100` | List paging |
| `Shortener:MaxUrlLength` | `2048` | Longest accepted URL |
| `Shortener:DeduplicateUrls` | `true` | Reuse the existing link for the same URL |
| `Shortener:ReservedAliases` | `api, health, openapi, swagger, admin, static` | Codes that cannot be claimed (configured values are added) |
| `Security:AllowPrivateNetworkTargets` | `true` in Development, otherwise `false` | Allow localhost/private targets |
| `Security:DeniedHosts` | empty | Hosts (and subdomains) that cannot be shortened |
| `Cache:LinkTtlSeconds` | `300` | How long a found link is cached |
| `Cache:MissingTtlSeconds` | `30` | How long an unknown code is remembered |
| `Cache:SizeLimit` | `10000` | Maximum cache entries |
| `Analytics:QueueCapacity` | `10000` | Clicks buffered before dropping |
| `Analytics:BatchSize` | `200` | Clicks written per database round trip |
| `Analytics:DefaultStatsWindowDays` | `30` | Statistics window when `from` is omitted |
| `Analytics:MaxStatsRangeDays` | `366` | Widest allowed statistics window |
| `Analytics:TopReferrerCount` | `10` | Referrers listed in statistics |
| `RateLimit:WritesPerMinutePerIp` | `30` | Token bucket for create, delete and stats |
| `RateLimit:ResolutionsPerMinutePerIp` | `600` | Fixed window for lookups |
| `RateLimit:QueueLimit` | `0` | Requests queued instead of rejected |

## 11. Decisions and trade-offs

| Decision | Why | Cost |
|---|---|---|
| `GET /{code}` returns `200` with the URL, not a redirect | Matches the reference repository; result is visible in Swagger and clients; every lookup is counted | Browsers do not follow the link by themselves |
| Random codes from a secure generator | Not guessable in sequence; no shared counter | Needs a unique index and retry on collision |
| Soft delete | Codes are never reused, history stays | Rows are never removed |
| Asynchronous, best-effort click recording | Lookups never wait for analytics | Clicks can be lost on crash or overload (counted and visible in metrics and health) |
| Negative cache for unknown codes | Guessing codes does not load the database | A new code can look missing on another instance for up to 30 seconds |
| Per-instance memory cache | Simple and fast | Deactivation can take up to 5 minutes to reach other instances |
| SQLite with EF Core migrations | No server to install; schema changes are versioned | One writer at a time; move to PostgreSQL/SQL Server for scale |
| Literal private-network checks, no DNS | No time-of-check/time-of-use gap; the service never fetches targets | A public name pointing at a private IP is accepted |
| `422` for rule violations, `400` for unreadable bodies | Clients can tell the two apart | Slightly more status codes to handle |
| Token bucket for writes | No double burst at window edges | Slightly more state per client |

## 12. Limitations

- No authentication or ownership of links.
- Click events live in memory until written; a crash loses queued clicks.
- Cache and rate limits are per instance; several instances need a shared cache (e.g. Redis) and a shared limiter.
- SQLite suits a single instance; there is no data retention job for click events.
- No URL reputation or malware checks.
- Lookups do not redirect, so short links are not directly usable in a browser address bar.

## 13. AI-assisted engineering

This repository was produced with an AI coding agent under the owner's direction. How AI was used, the options it
proposed and the ones that were rejected, the quality gates, and the sign-off checklist are in
[docs/AI_ASSISTED_ENGINEERING.md](docs/AI_ASSISTED_ENGINEERING.md). Worked greenfield, brownfield and
ambiguous-requirement scenarios are in [docs/SCENARIOS.md](docs/SCENARIOS.md). The owner's review and sign-off is the
remaining step before any production use.

## 14. Repository layout

```
.github/workflows/ci.yml            format, build, test with coverage, vulnerability scan
docs/                               engineering scenarios and AI-usage log
src/UrlShortener.Core/              domain, options, safety rules, service
src/UrlShortener.Infrastructure/    EF Core, migrations, cache, click queue, health, metrics
src/UrlShortener.Api/               endpoints, contracts, errors, rate limits, correlation ids, Swagger
tests/UrlShortener.UnitTests/       service, rules, cache, queue and architecture tests
tests/UrlShortener.IntegrationTests/ API and SQLite tests
Dockerfile                          multi-stage build, non-root runtime, /data volume
dotnet-tools.json                   dotnet-ef for migrations
```
