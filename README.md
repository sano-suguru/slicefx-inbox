# SliceFx Inbox

Personal read-later inbox dogfooding [SliceFx](https://github.com/sano-suguru/slicefx) on Fermyon Cloud (Spin WASI).

**Live app:** https://slicefx-inbox-1gat4stw.fermyon.app

## What it does

- Save URLs for later reading (OG title auto-fetched)
- Subscribe to RSS / Atom feeds with automatic refresh (every 30 minutes)
- Filter items by keyword, tag, or read status
- Multi-workspace: each visitor gets a private anonymous workspace via an opaque token

## Getting started

Open the app and choose one of:

| Option | Description |
|---|---|
| **Create workspace** | Get a new private inbox. Token shown once — save it. |
| **Paste token** | Restore an existing workspace from a saved token. |
| **Try demo** | Explore a shared demo workspace (public read-write). |

Your workspace token is stored in `sessionStorage` only — it is never sent anywhere except this app and clears when you close the tab.

## Running locally

```bash
# Build WASM (linux-x64 or win-x64 only; macOS uses Docker)
docker run --rm --platform linux/amd64 -v "$PWD":/work -w /work \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish src/Inbox.Server -r wasi-wasm -c Release

# Build Blazor client
dotnet publish src/Inbox.Client -c Release

# Run (requires Spin CLI + trigger-cron plugin)
SPIN_VARIABLE_CRON_TOKEN=dev-secret spin up --file src/Inbox.Server/spin.toml
# → SPA at http://localhost:3000/   API at http://localhost:3000/api/...
```

## API overview

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/workspaces` | — | Create anonymous workspace, returns token (once) |
| POST | `/api/demo` | — | Get shared demo workspace token |
| GET | `/api/items` | `X-Workspace-Token` | List items (filters: `?q=`, `?tag=`, `?status=`) |
| POST | `/api/items` | `X-Workspace-Token` | Save URL (OG title fetched server-side) |
| PATCH | `/api/items/{id}` | `X-Workspace-Token` | Update status / tags |
| DELETE | `/api/items/{id}` | `X-Workspace-Token` | Remove item |
| GET | `/api/feeds` | `X-Workspace-Token` | List feed subscriptions |
| POST | `/api/feeds` | `X-Workspace-Token` | Subscribe to RSS/Atom feed |
| POST | `/api/feeds/refresh` | `X-Workspace-Token` | Refresh this workspace's feeds |
| POST | `/api/feeds/refresh-all` | `X-Cron-Token` | Admin: refresh all workspaces |

## Stack

- **Runtime:** [SliceFx](https://github.com/sano-suguru/slicefx) on [Fermyon Spin](https://spinframework.dev/) (WASI / NativeAOT-LLVM)
- **Storage:** Spin KV store (`wasi:keyvalue`)
- **Frontend:** Blazor WASM served via `spin-fileserver`
- **Scheduling:** GitHub Actions (feed refresh every 30 min)

## Known limitations

- Tokens are stored raw in KV (no hashing — `System.Security.Cryptography` unavailable in WASI NativeAOT-LLVM). KV read access = full token exposure.
- `Guid.NewGuid()` entropy quality is unconfirmed in this WASI runtime. Token uses two GUIDs concatenated for collision margin.
- `workspaces:index` append has a read-modify-write race on concurrent registration; affected workspace still authenticates fine but may be skipped by cron until the next deploy.
- Demo workspace is shared read-write — all visitors can see and modify its content. Server-side OG fetch can be triggered anonymously via demo token.
- Public self-registration can be disabled by setting `registration_open=false` via `spin cloud variables set`. Hard cap at 1000 workspaces.
- Old global KV keys (`item:*`, `items:index`) from before multi-workspace migration are dead bytes in the store.
