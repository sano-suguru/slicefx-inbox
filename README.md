# slicefx-inbox

[日本語](README.ja.md)

Personal read-later + RSS inbox — dogfooding [SliceFx](https://github.com/sano-suguru/slicefx) on [Fermyon Cloud](https://cloud.fermyon.com) (Spin WASI).

[![Deployed on Fermyon Cloud](https://img.shields.io/badge/Fermyon_Cloud-live-brightgreen)](https://slicefx-inbox-1gat4stw.fermyon.app/)

![SliceFx Inbox SPA](docs/screenshot.png)

## What it does

- **Bookmark**: `POST /api/items {url}` → saves to Spin key-value, OG title auto-fetched
- **Read-later list**: `GET /api/items` (filter: `?q=`, `?tag=`, `?status=`), `GET /api/items/{id}`, `DELETE /api/items/{id}`
- **Tags & status**: `PATCH /api/items/{id} {status?, tags?}` — mark read/archived, add tags
- **SPA** (Blazor WASM, served same-origin via `spin-fileserver`):
  Login page (create workspace / paste token / try demo), item list + search/filter,
  add URL, mark read/archive/delete, feed subscribe, manual refresh.
- **Multi-workspace**: each user gets a private anonymous workspace via an opaque server-issued token.
  All endpoints (including GETs) require `X-Workspace-Token`.
- **RSS auto-import** via Spin cron trigger (local) / GitHub Actions scheduler (Fermyon Cloud)

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

# 4. Run with Spin (cron_token is required; any value works for local testing)
#    spin.toml includes a cron trigger — install the plugin once if needed:
#    spin plugin install trigger-cron
SPIN_VARIABLE_CRON_TOKEN=dev-secret spin up --file src/Inbox.Server/spin.toml
```

Open `http://localhost:3000/` in a browser.
- `/login` — create a workspace or try the demo to get a token.
- `/` serves the SPA; `/api/...` is the API (same-origin, no CORS needed).

### API smoke test (curl)

```bash
# Create a workspace (no auth required) — returns token once
TOKEN=$(curl -fsS -X POST http://localhost:3000/api/workspaces | jq -r .Token)

# Add an item
curl -X POST http://localhost:3000/api/items \
     -H "Content-Type: application/json" \
     -H "X-Workspace-Token: $TOKEN" \
     -d '{"url":"https://example.com"}'

# List items
curl http://localhost:3000/api/items -H "X-Workspace-Token: $TOKEN"

# Try the shared demo workspace
DEMO_TOKEN=$(curl -fsS -X POST http://localhost:3000/api/demo | jq -r .Token)
curl http://localhost:3000/api/items -H "X-Workspace-Token: $DEMO_TOKEN"
```

## Deploy to Fermyon Cloud

```bash
# 1. Publish the Blazor WASM client
dotnet publish src/Inbox.Client -c Release

# 2. Publish the WASI server component
#    Requires a linux-x64 or win-x64 host. On macOS, use Docker linux/amd64:
docker run --rm --platform linux/amd64 -v "$PWD":/work -w /work \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish src/Inbox.Server -r wasi-wasm -c Release
#    Outputs: src/Inbox.Server/dist/inbox-server.wasm

# 3. Set the admin cron token (required; used by GitHub Actions feed refresh)
spin cloud variables set --app slicefx-inbox cron_token=<secret>

# 4. Deploy (uses spin.cloud.toml — HTTP-only, no cron trigger)
spin cloud login          # first time only
spin cloud deploy --file src/Inbox.Server/spin.cloud.toml
```

GitHub Actions secret `INBOX_CRON_TOKEN` must match the `cron_token` variable above.

> Fermyon Cloud does not support cron triggers; feed refresh is handled by a GitHub Actions
> schedule (`.github/workflows/feed-refresh.yml`, every 30 min) calling
> `POST /api/feeds/refresh-all` with `X-Cron-Token`.

## SliceFx packages used

| Package | Version |
|---|---|
| `SliceFx.Core` | 0.1.0-preview.9 |
| `SliceFx.Wasi` | 0.1.0-preview.9 |
| `SliceFx.Wasi.KeyValue` | 0.1.0-preview.9 |
| `SliceFx.Wasi.HttpClient` | 0.1.0-preview.9 |
| `SliceFx.Wasi.Spin` | 0.1.0-preview.9 |
| `SliceFx.SourceGenerator` | 0.1.0-preview.9 |

## What's working

- ✅ **Bookmark** — POST/GET/DELETE items, Spin key-value store
- ✅ **Read-later list & filters** — `?q=`, `?tag=`, `?status=`; Blazor WASM SPA (same-origin via `spin-fileserver`)
- ✅ **Tags & status** — PATCH `/api/items/{id}`; mark read, archived, add/remove tags
- ✅ **RSS auto-import** — subscribe feeds; cron trigger (local Spin) / GitHub Actions scheduler (Fermyon Cloud)
- ✅ **Multi-workspace auth** — per-workspace opaque tokens via KV lookup; all endpoints auth-gated
- ✅ **Workspace isolation** — each workspace's data is keyed under `w:{wid}:*`; isolation verified by tests
- ✅ **In-process tests + CI** — xUnit handler tests; GitHub Actions build/test gate
- ✅ **OG title fetch** — `POST /api/items` fetches `og:title` / `<title>` from the saved URL (best-effort)
- ✅ **Fully generated typed client** — `SliceApiClient.g.cs` generated by `slicefx client csharp`

See [CLAUDE.md](CLAUDE.md) for implementation notes.

## Known limitations

- **OG title fetch is best-effort**: https redirects followed up to 3 hops; http:// not followed.
  UTF-8 decode only — non-UTF-8 pages may produce garbled titles. URL used as fallback on failure.
- **Tokens stored raw in KV**: `System.Security.Cryptography` unavailable in WASI NativeAOT-LLVM,
  so tokens cannot be hashed. KV read access = full token exposure.
- **`Guid.NewGuid()` entropy unconfirmed** in this WASI runtime. Token uses two GUIDs concatenated
  for collision margin — this improves collision resistance but not prediction resistance if the
  RNG is weak.
- **KV listing via prefix scan**: item, feed, and workspace listings are derived by
  `get-keys` prefix scan rather than mutable index keys, eliminating the read-modify-write
  lost-update race. The trade-off is O(total keys) per list call (acceptable at dogfood scale).
  Concurrent feed refresh (cron + manual) can still ingest the same entry twice under a race
  (dedup is best-effort snapshot). Workspace count enforcement (`MaxWorkspaces`) retains the
  same TOCTOU caveat as before.
- **Demo is shared read-write**: all visitors get the same demo token and can mutate/delete content.
  Server-side OG fetch can be triggered anonymously via the demo token.
- **Public self-registration** can be disabled: `spin cloud variables set registration_open=false`.
  Hard cap at 1000 workspaces as an additional abuse guard.
