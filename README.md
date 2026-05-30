# slicefx-inbox

Personal read-later + RSS inbox — dogfooding [SliceFx](https://github.com/sano-suguru/slicefx) on [Fermyon Cloud](https://cloud.fermyon.com) (Spin WASI).

[![Deployed on Fermyon Cloud](https://img.shields.io/badge/Fermyon_Cloud-live-brightgreen)](https://slicefx-inbox-1gat4stw.fermyon.app/)

![SliceFx Inbox SPA](docs/screenshot.png)

## What it does

- **Bookmark**: `POST /api/items {url}` → saves to Spin key-value
- **Read-later list**: `GET /api/items` (filter: `?q=`, `?tag=`, `?status=`), `GET /api/items/{id}`, `DELETE /api/items/{id}`
- **Tags & status**: `PATCH /api/items/{id} {status?, tags?}` — mark read/archived, add tags
- **SPA** (Blazor WASM, served same-origin via `spin-fileserver`):
  item list + search/filter, add URL, mark read/archive/delete, feed subscribe, manual refresh.
  Settings page (`/settings`) for the operator refresh token (runtime-only, stored in sessionStorage — never in the build artifact).
- *(Increment B)* RSS auto-import via Spin cron trigger

## Requirements

- .NET 10 SDK (`10.0.300`)
- [Spin CLI](https://developer.fermyon.com/spin/install) — for local runs and Fermyon Cloud deploys

## Quick start

### Build & run locally

```bash
# 1. Build the solution (non-WASI)
dotnet build Inbox.slnx

# 2. Publish the WASI server component (-> dist/inbox-server.wasm)
#    linux-x64 / win-x64 host required; macOS: use Docker linux/amd64
dotnet publish src/Inbox.Server -r wasi-wasm -c Release

# 3. Publish the Blazor WASM client (served by spin-fileserver)
dotnet publish src/Inbox.Client -c Release

# 4. Run with Spin (refresh_token is required; any value works for local testing)
#    spin.toml includes a cron trigger — install the plugin once if needed:
#    spin plugin install trigger-cron
SPIN_VARIABLE_REFRESH_TOKEN=<token> spin up --file src/Inbox.Server/spin.toml
```

Open `http://localhost:3000/` in a browser.
- `/` serves the SPA; `/api/...` is the API (same-origin, no CORS needed).
- Go to **Settings** (`/settings`) and enter your refresh token to enable write actions
  (add items, update status/tags, delete, subscribe feeds, trigger refresh).

### API smoke test (curl)

```bash
# List items (no auth required)
curl http://localhost:3000/api/items

# Add an item (requires X-Refresh-Token header)
curl -X POST http://localhost:3000/api/items \
     -H "Content-Type: application/json" \
     -H "X-Refresh-Token: <token>" \
     -d '{"url":"https://example.com"}'
```

## Deploy to Fermyon Cloud

```bash
# 1. Publish the Blazor WASM client
dotnet publish src/Inbox.Client -c Release

# 2. Deploy (uses spin.cloud.toml — HTTP-only, no cron trigger)
spin cloud login          # first time only
spin cloud deploy --file src/Inbox.Server/spin.cloud.toml
```

`refresh_token` is required — set it before the first deploy or the app will fail to start:

```bash
spin cloud variables set --app slicefx-inbox refresh_token=<value>
```

> Fermyon Cloud does not support cron triggers; feed refresh is handled by a GitHub Actions
> schedule (`.github/workflows/feed-refresh.yml`) that calls `POST /api/feeds/refresh`.

## SliceFx packages used

| Package | Version |
|---|---|
| `SliceFx.Core` | 0.1.0-preview.5 |
| `SliceFx.Wasi` | 0.1.0-preview.5 |
| `SliceFx.Wasi.KeyValue` | 0.1.0-preview.5 |
| `SliceFx.Wasi.HttpClient` | 0.1.0-preview.5 |
| `SliceFx.Wasi.Spin` | 0.1.0-preview.5 |
| `SliceFx.SourceGenerator` | 0.1.0-preview.5 |

## Status

- **A** ✅ API (POST/GET/DELETE items, KV store)
- **A.5** ✅ Blazor WASM SPA + spin-fileserver route split (shipped 2026-05-30)
- **B** ✅ RSS auto-import, cron trigger, auth (Spin variables)
- **C** ✅ Tags, PATCH status/tags, GET filters
- **E** ✅ Polish + v1 readiness (in-process tests, CI, framework gap fixes upstream)

See [CLAUDE.md](CLAUDE.md) for implementation notes.

## Known limitations

- **OG title fetch disabled**: WASI outgoing HTTP is incompatible with in-process dispatch in
  preview.5. Item URL is used as title instead.
- **GET endpoints are unauthenticated**: `GET /api/items`, `GET /api/items/{id}`, `GET /api/feeds`
  are intentionally open — read-only public content.
- **Single shared auth token**: The refresh token is a Spin application variable. No per-user
  identity or token rotation.
- **`WasiResponse` routes not in typed client**: Handlers returning `Task<WasiResponse>` cannot
  be auto-generated into a typed HTTP client (framework design). `SliceApiClient.cs` is
  hand-written. See [slicefx#3](https://github.com/sano-suguru/slicefx/issues/3) (fixed upstream).
- **Null nullable query param fix deferred**: The upstream fix for emitting `null` query params
  as absent (not `"name="`) requires publishing SliceFx preview.6. Workaround in `GetItems.cs`
  (`string.IsNullOrEmpty` guards) is correct semantics and stays in place.
  See [slicefx#4](https://github.com/sano-suguru/slicefx/issues/4) (fixed upstream).
