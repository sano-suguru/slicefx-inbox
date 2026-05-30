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

# Local Spin run (requires Spin CLI + trigger-cron plugin installed)
spin up --file src/Inbox.Server/spin.toml

# Fermyon Cloud deploy (omits the cron trigger block — Cloud does not support cron)
spin cloud deploy --file src/Inbox.Server/spin.cloud.toml

# Smoke test (app must be running on :3000)
curl -X POST http://localhost:3000/api/items \
     -H "Content-Type: application/json" \
     -d '{"url":"https://example.com"}'
curl http://localhost:3000/api/items
```

## Deploys

- Local Spin port: 3000 (default)
- Fermyon Cloud URL: https://slicefx-inbox-1gat4stw.fermyon.app
- Fermyon Cloud token: stored via `spin cloud login` (not committed)
- Metrics: Fermyon dashboard → app "slicefx-inbox" → Logs / Request count

## Observability

```bash
spin cloud logs slicefx-inbox   # Fermyon log tail
```

## Architecture

One-file-one-feature (SliceFx pattern):
```
src/Inbox.Server/
  Features/Feeds/AddFeed.cs        POST /api/feeds
  Features/Feeds/GetFeeds.cs       GET /api/feeds
  Features/Feeds/RefreshFeeds.cs   POST /api/feeds/refresh  (auth required)
  Features/Items/PostItem.cs       POST /api/items  (auth required)
  Features/Items/GetItems.cs       GET /api/items  (?q= ?tag= ?status= optional filters)
  Features/Items/GetItem.cs        GET /api/items/{id}
  Features/Items/UpdateItem.cs     PATCH /api/items/{id}  (auth required; partial update status/tags)
  Features/Items/DeleteItem.cs     DELETE /api/items/{id}  (auth required)
  Infrastructure/FeedParser.cs           RSS/Atom feed parser
  Infrastructure/FeedRefreshCronHandler.cs  ISpinCronHandler impl
  Infrastructure/ITokenGuard.cs          auth token abstraction (ITokenGuard interface + TokenAuth.SafeEquals)
  Infrastructure/RefreshTokenGuard.cs    ITokenGuard impl — reads "refresh_token" via ISpinVariables + constant-time compare
  Infrastructure/ItemStatus.cs           status vocabulary constants (unread / read / archived)
  Infrastructure/SpinKeyValueStore.cs    wasi:keyvalue WIT-bound IKeyValueStore impl
  Infrastructure/SpinVariables.cs        fermyon:spin/variables WIT-bound ISpinVariables impl
  Infrastructure/SpinWasiHttpClient.cs   wasi:http/outgoing-handler IWasiHttpClient impl
  IncomingHandlerImpl.cs             wasi:http/incoming-handler bridge
  CronHandlerBridge.cs               world-level handle-cron-event bridge
  InboxJsonContext.cs                source-gen JSON context
  spin.toml                          Local Spin manifest (includes cron trigger)
  spin.cloud.toml                    Fermyon Cloud manifest (HTTP only; cron omitted)
```

KV key scheme:
- `item:{id}` → JSON serialized `InboxItem` (fields: Id, Url, Title, Description, Status, SavedAt, Source, Tags)
- `items:index` → JSON array of IDs (insertion order)
- `feed:{id}` → JSON serialized `FeedSubscription`
- `feeds:index` → JSON array of feed IDs (insertion order)

Status vocabulary (`ItemStatus.cs`): `unread` (default) / `read` / `archived`

## Increment roadmap (see main plan for full detail)

- **A** ✅: API only (POST/GET/DELETE items, KV store)
- **A.5**: Blazor UI + `spin-fileserver` route split (SPA on `/`, API on `/api/*`)
- **B** ✅: RSS feeds + `SliceFx.Wasi.Spin` satellite (cron trigger) + GH Actions scheduler + auth (Spin variables)
- **C** ✅: Tags on `InboxItem`, PATCH status/tags, `GET /api/items` filters (?q=, ?tag=, ?status=)
- **E**: Polish + v1 readiness assessment

---

## WASI implementation notes (lessons from Increment B, incorporated in preview.5)

Reference impl context: `SpinVariables.cs` (`Infrastructure/SpinVariables.cs`) implements
`ISpinVariables` (from `SliceFx.Wasi.Spin` preview.5+) using the raw WIT binding internally.
Auth is encapsulated in `ITokenGuard` / `RefreshTokenGuard` — the old `ISecrets.cs` no longer
exists. `TokenAuth.SafeEquals` (constant-time comparison) lives in `Infrastructure/ITokenGuard.cs`.

### Spin variables binding shape

`fermyon:spin/variables@2.0.0` generates a free function on a `*Interop` static class:

```csharp
using VariablesInterop = ProxyWorld.wit.imports.fermyon.spin.v2_0_0.VariablesInterop;
// VariablesInterop.Get(name) returns string or throws WitException<IVariables.Error>
// IVariables holds only the Error type — it is NOT the call entry point.
```

`SliceFx.Wasi.Spin` (preview.5+) exposes `ISpinVariables` / `InMemorySpinVariables` as a
higher-level abstraction over this pattern (fail-closed, async surface over sync WIT).

### System.Security.Cryptography unavailable in WASI

`System.Security.Cryptography` (including `CryptographicOperations.FixedTimeEquals`) is absent
in NativeAOT-LLVM WASI builds. `Infrastructure/ITokenGuard.cs::TokenAuth.SafeEquals` uses a manual
XOR-accumulation loop for constant-time token comparison. See `docs/patterns/platform-abstraction.md`
in `~/dev/slicefx` for the canonical workaround pattern.

### Cron trigger wiring

- WIT export is world-level `handle-cron-event` via `IProxyWorld` (NOT `IIncomingHandler`).
- `async func` exports fail component encoding in componentize-dotnet 0.7.0-preview → use sync `func`.
- Cron expression: **6 fields** `{sec} {min} {hour} {dom} {month} {dow}` — 7 fields → ParseSchedule error.
- `spin.toml` uses `0 */1 * * * *` (every minute) for local testing convenience.
  Production refresh is driven by GitHub Actions (`*/30 * * * *`, every 30 min) calling
  `POST /api/feeds/refresh` — Fermyon Cloud does not support cron triggers natively.
