# CLAUDE.md — slicefx-inbox

## What this is

Personal read-later + RSS inbox app, dogfooding [SliceFx](https://github.com/sano-suguru/slicefx) on Fermyon Cloud (Spin WASI).

Deploy target: Fermyon Cloud free tier (5 apps / 100K req/mo / 100 MB component limit).
Backend: `src/Inbox.Server/` — SliceFx WASI component.
Frontend: `src/Inbox.Client/` — Blazor WASM (Increment A.5, not yet).

## SliceFx reference rules

- **NuGet only — `<ProjectReference>` to slicefx/ is prohibited** during Increment A (pack-path bug detection).
- Increment B onward: local NuGet feed (`dotnet pack && dotnet nuget push --source <local>`) is allowed as an escape hatch if the publish→version-bump roundtrip becomes a bottleneck.
- When you find a SliceFx bug: switch to `~/dev/slicefx` session → fix → `gh workflow run publish.yml` (or local feed push) → bump `<PackageReference Version>` here.
- Paired plan file (slicefx main session): `~/.claude/plans/slicefx-concurrent-deer.md`

## Commands

```bash
# Regular build (non-WASI, no WIT bindings needed)
dotnet build Inbox.slnx

# WASI publish — requires linux-x64 or win-x64 host; on macOS use Docker linux/amd64
dotnet publish src/Inbox.Server -r wasi-wasm -c Release
# Copies output to src/Inbox.Server/dist/inbox-server.wasm

# Local Spin run (requires Spin CLI installed)
spin up --file src/Inbox.Server/spin.toml

# Fermyon Cloud deploy
spin cloud deploy --file src/Inbox.Server/spin.toml

# Smoke test (app must be running on :3000)
curl http://localhost:3000/api/spike/outbound          # Spike 1 verification
curl -X POST http://localhost:3000/api/items \
     -H "Content-Type: application/json" \
     -d '{"url":"https://example.com"}'
curl http://localhost:3000/api/items
```

## Active spikes (Increment A)

### Spike 1: outbound HttpClient via wasi:http/outgoing-handler

**Status**: unverified  
**How to test**: after `spin up`, `curl http://localhost:3000/api/spike/outbound`  
**Pass**: `{"status":"ok","title":"...", "error":null}` — componentize-dotnet auto-maps SocketsHttpHandler  
**Fail**: `{"status":"unreachable","title":"unreachable","error":"..."}` → create `SliceFx.Wasi.HttpClient` satellite in `~/dev/slicefx`

### Spike 2: wasi:keyvalue WIT bindings for Spin/Fermyon

**Status**: unverified  
**Current state**: `InMemoryKeyValueStore` is used (data lost between invocations)  
**How to complete**: see `src/Inbox.Server/Infrastructure/SpinKeyValueStore.cs` for step-by-step instructions  
**Pass**: `POST /api/items` → `GET /api/items` returns the item after a cold restart

## Deploys

- Local Spin port: 3000 (default)
- Fermyon Cloud URL: (fill in after first `spin cloud deploy`)
- Fermyon Cloud token: stored via `spin cloud login` (not committed)
- Metrics: Fermyon dashboard → app "slicefx-inbox" → Logs / Request count

## Observability

```bash
spin cloud logs slicefx-inbox   # Fermyon log tail
```

Data backup (after spike 2):
```bash
# TODO: add KV export command here once SpinKeyValueStore is implemented
```

## Architecture

One-file-one-feature (SliceFx pattern):
```
src/Inbox.Server/
  Features/Items/PostItem.cs      POST /api/items
  Features/Items/GetItems.cs      GET /api/items
  Features/Items/GetItem.cs       GET /api/items/{id}
  Features/Items/DeleteItem.cs    DELETE /api/items/{id}
  Features/Spikes/GetOutboundTest.cs  GET /api/spike/outbound  (remove after spike 1)
  Infrastructure/SpinKeyValueStore.cs  (TODO: implement after spike 2)
  IncomingHandlerImpl.cs          wasi:http/incoming-handler bridge
  InboxJsonContext.cs             source-gen JSON context
  spin.toml                       Spin component manifest
```

KV key scheme:
- `item:{id}` → JSON serialized `InboxItem`
- `items:index` → JSON array of IDs (insertion order)

## Increment roadmap (see main plan for full detail)

- **A** (current): API only — `POST /api/items`, `GET /api/items`, `GET /api/items/{id}`, `DELETE /api/items/{id}`
- **A.5**: Blazor UI + `spin-fileserver` route split (SPA on `/`, API on `/api/*`)
- **B**: RSS feeds + `SliceFx.Wasi.Spin` satellite (cron trigger)
- **C**: Search, tags, read/archive management
- **D**: Bearer auth + Spin variables
- **E**: Polish + v1 readiness assessment
