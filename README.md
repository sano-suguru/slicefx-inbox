# slicefx-inbox

Personal read-later + RSS inbox — dogfooding [SliceFx](https://github.com/sano-suguru/slicefx) on [Fermyon Cloud](https://cloud.fermyon.com) (Spin WASI).

## What it does

- **Bookmark**: `POST /api/items {url}` → fetches page title → saves to Spin key-value
- **Read-later list**: `GET /api/items`, `GET /api/items/{id}`, `DELETE /api/items/{id}`
- *(Increment A.5)* Blazor WASM UI — same origin as the API via `spin-fileserver`
- *(Increment B)* RSS auto-import via Spin cron trigger

## Requirements

- .NET 10 SDK (`10.0.300`)
- [Spin CLI](https://developer.fermyon.com/spin/install) — for local runs and Fermyon Cloud deploys

## Quick start

```bash
# Build (non-WASI)
dotnet build Inbox.slnx

# WASI publish (linux-x64 or win-x64 host required; macOS: use Docker linux/amd64)
dotnet publish src/Inbox.Server -r wasi-wasm -c Release

# Local run
spin up --file src/Inbox.Server/spin.toml

# Smoke test
curl -X POST http://localhost:3000/api/items \
     -H "Content-Type: application/json" \
     -d '{"url":"https://example.com"}'
curl http://localhost:3000/api/items
```

## Deploy to Fermyon Cloud

```bash
spin cloud login
spin cloud deploy --file src/Inbox.Server/spin.toml
```

## SliceFx packages used

| Package | Version |
|---|---|
| `SliceFx.Core` | 0.1.0-preview.2 |
| `SliceFx.Wasi` | 0.1.0-preview.2 |
| `SliceFx.Wasi.KeyValue` | 0.1.0-preview.2 |
| `SliceFx.SourceGenerator` | 0.1.0-preview.2 |

## Status

Increment A — API MVP (in progress). See [CLAUDE.md](CLAUDE.md) for active spikes.
