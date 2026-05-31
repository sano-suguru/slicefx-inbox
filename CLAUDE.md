# CLAUDE.md — slicefx-inbox

## What this is

Personal read-later + RSS inbox app, dogfooding [SliceFx](https://github.com/sano-suguru/slicefx) on Fermyon Cloud (Spin WASI).

Deploy target: Fermyon Cloud free tier (5 apps / 100K req/mo / 100 MB component limit).
Backend: `src/Inbox.Server/` — SliceFx WASI component.
Frontend: `src/Inbox.Client/` — Blazor WASM SPA (Increment A.5, shipped).

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

# Build Blazor WASM client (required before spin up / deploy)
dotnet publish src/Inbox.Client -c Release

# Local Spin run (requires Spin CLI + trigger-cron plugin installed)
# SPA served at http://localhost:3000/, API at http://localhost:3000/api/...
SPIN_VARIABLE_REFRESH_TOKEN=<token> spin up --file src/Inbox.Server/spin.toml

# Fermyon Cloud deploy (omits the cron trigger block — Cloud does not support cron)
spin cloud deploy --file src/Inbox.Server/spin.cloud.toml

# Smoke test (app must be running on :3000)
curl -X POST http://localhost:3000/api/items \
     -H "Content-Type: application/json" \
     -H "X-Refresh-Token: <token>" \
     -d '{"url":"https://example.com"}'
curl http://localhost:3000/api/items

# Dogfood CLI evidence (A.5-2) — regen after DTO changes
dotnet build Inbox.slnx
dotnet tool run slicefx -- client csharp --project src/Inbox.Server \
  --namespace Inbox.Client --output src/Inbox.Client/SliceApiClient.evidence.g.cs --force
```

## Deploys

- Local Spin port: 3000 (SPA at `/`, API at `/api/...`)
- Fermyon Cloud URL: https://slicefx-inbox-1gat4stw.fermyon.app (SPA at `/`, API at `/api/...`)
- Fermyon Cloud token: stored via `spin cloud login` (not committed)
- Metrics: Fermyon dashboard → app "slicefx-inbox" → Logs / Request count

## Observability

```bash
spin cloud logs slicefx-inbox   # Fermyon log tail
```

## Architecture

One-file-one-feature (SliceFx pattern). Route split: `spin-fileserver` at `/...` (SPA), `inbox-server` at `/api/...` (API). Same-origin — no CORS. Operator token entered at runtime in SPA Settings page; held in `sessionStorage` + in-memory `RefreshTokenHolder`; injected via `RefreshTokenHandler : DelegatingHandler` as `X-Refresh-Token`.

```
src/Inbox.Contracts/
  ItemContracts.cs       InboxItem, FeedSubscription, ItemStatus (public)
  RequestContracts.cs    all boundary DTOs (form-backers: mutable { get; set; }; responses: positional)

src/Inbox.Client/        Blazor WASM SPA
  Program.cs             DI: named HttpClient + RefreshTokenHandler + RefreshTokenHolder + ISessionStorage
  SliceApiClient.cs      hand-written typed client (same-origin /api/...)
  SliceApiClient.evidence.g.cs  dogfood CLI output — evidence only, NOT compiled in
  RefreshTokenHandler.cs DelegatingHandler injecting X-Refresh-Token
  RefreshTokenHolder.cs  singleton in-memory token + sessionStorage hydration
  SessionStorage.cs      thin IJSRuntime wrapper over sessionStorage
  Layout/MainLayout.razor nav + token-missing banner
  Pages/Items.razor       / — item list + add URL form + filter
  Pages/ItemDetail.razor  /items/{id} — single item (SPA deep-link target)
  Pages/Feeds.razor       /feeds — feed list + subscribe + manual refresh
  Pages/Settings.razor    /settings — token entry/clear
  Components/InboxItemCard.razor  item card with read/tag/delete actions

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
  Infrastructure/ItemStatus.cs           (removed — promoted to Inbox.Contracts.ItemStatus)
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

Status vocabulary (`Inbox.Contracts.ItemStatus`): `unread` (default) / `read` / `archived`

## Increment roadmap (see main plan for full detail)

- **A** ✅: API only (POST/GET/DELETE items, KV store)
- **A.5** ✅: Blazor WASM SPA + `spin-fileserver` route split (SPA at `/`, API at `/api/...`). DTOs moved to `Inbox.Contracts`; `ItemStatus` promoted to public; hand-written `SliceApiClient.cs`; operator enters refresh token in SPA settings at runtime (never in build artifact). Spike confirmed: Spin passes full `:path` to wasi:http component — no strip-compensate needed.
- **B** ✅: RSS feeds + `SliceFx.Wasi.Spin` satellite (cron trigger) + GH Actions scheduler + auth (Spin variables)
- **C** ✅: Tags on `InboxItem`, PATCH status/tags, `GET /api/items` filters (?q=, ?tag=, ?status=)
- **E**: Polish + v1 readiness assessment
  - v1 readiness verdict: **correctness blockers: none**. All mutating endpoints are auth-gated
    (`ITokenGuard`). Error handling: 413/500 in `IncomingHandlerImpl.cs:35-43`, RFC-7807-style
    `SliceResult.Problem` in handlers. Body limit: 1 MB (`IncomingHandlerImpl.cs:12`).
    Framework gaps discovered via dogfood fixed upstream (see below).
  - Checklist:
    - [x] framework gap (a)(b) fixed in slicefx (https://github.com/sano-suguru/slicefx/issues/3,
          https://github.com/sano-suguru/slicefx/issues/4; commit de1e953 on slicefx main)
    - [x] xUnit in-process handler tests (`tests/Inbox.Server.Tests/`) — CI green
    - [x] push + PR build/test CI (`.github/workflows/ci.yml`)
    - [x] README Status updated to reflect E completion
    - [x] known-limitations documented (see README Known limitations)
  - Known limitations (by design or accepted constraints, not blockers):
    - OG title fetch implemented (best-effort, fail-open) in `PostItem.cs`. Constraints: no
      redirect following (301/302 falls back to URL-as-title); UTF-8 decode only (non-UTF-8 pages
      may produce garbled titles). Both match `RefreshFeeds.cs` parity.
    - GET endpoints (`GET /api/items`, `GET /api/item/{id}`, `GET /api/feeds`) are intentionally
      unauthenticated — read-only, public content.
    - Auth token is a single shared Spin variable (`refresh_token`). No per-user identity.
    - `WasiResponse`-returning handlers cannot be auto-generated into typed clients (by design —
      `WasiResponse` is a server-side transport record). `SliceApiClient.cs` is hand-written.
      A generated `SliceApiClient.evidence.g.cs` is kept as a non-compiled dogfood artifact.
    - Incorporating upstream gap fixes (preview.6 packages + CLI bump) is complete as of preview.6.

---

### Framework gaps (fixed upstream in slicefx@de1e953, shipped in preview.6)

Two correctness gaps discovered via this dogfood app were fixed in the slicefx framework:

**gap (a)** — `slicefx client csharp/typescript/openapi` generated broken methods for
`WasiResponse`-returning routes. Fixed: these routes are now excluded from typed client generation
with a notice. Tracking: https://github.com/sano-suguru/slicefx/issues/3

**gap (b)** — C# client emitted `null` nullable query params as `"name="` (empty); WASI binder
treated `"name="` as `Bound` for nullable value types. Fixed: client omits null nullable params;
binder returns `Missing` for empty nullable value-type. Tracking:
https://github.com/sano-suguru/slicefx/issues/4

`GetItems.cs`'s `string.IsNullOrEmpty` guards remain correct (intentional semantics: empty = no
filter). The fix applies to nullable value-type params (`int?`, `Guid?`, etc.) and future callers.
Both fixes shipped in `0.1.0-preview.6`. The inbox now uses preview.6 packages; the evidence is
reflected in `SliceApiClient.evidence.g.cs` (regenerated: 6 `WasiResponse`-returning routes now
emit `// skipped (untyped WasiResponse)` notices instead of broken client methods).

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

### TimeSpan.FromMilliseconds int/long overload unavailable in WASI

.NET 7 で追加された `TimeSpan.FromMilliseconds(long)` は NativeAOT-LLVM WASI ビルドで不在。
`FromMilliseconds(250)` は int→long に解決され、ILC が `.cctor() will always throw` を emit、
当該型が実行時に初期化失敗する。double リテラル `FromMilliseconds(250.0)` で元からある
double overload を選ぶこと。発見: `HtmlMetadataParser.cs` の NativeAOT WASI ビルド時 (79979ab)。

### Cron trigger wiring

- WIT export is world-level `handle-cron-event` via `IProxyWorld` (NOT `IIncomingHandler`).
- `async func` exports fail component encoding in componentize-dotnet 0.7.0-preview → use sync `func`.
- Cron expression: **6 fields** `{sec} {min} {hour} {dom} {month} {dow}` — 7 fields → ParseSchedule error.
- `spin.toml` uses `0 */1 * * * *` (every minute) for local testing convenience.
  Production refresh is driven by GitHub Actions (`*/30 * * * *`, every 30 min) calling
  `POST /api/feeds/refresh` — Fermyon Cloud does not support cron triggers natively.
