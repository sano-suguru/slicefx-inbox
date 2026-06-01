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
SPIN_VARIABLE_CRON_TOKEN=<admin-token> spin up --file src/Inbox.Server/spin.toml

# Fermyon Cloud deploy (omits the cron trigger block — Cloud does not support cron)
spin cloud deploy --file src/Inbox.Server/spin.cloud.toml

# Smoke test (app must be running on :3000)
# 1. Create a workspace and save the token
curl -X POST http://localhost:3000/api/workspaces | jq .
# 2. Use the returned token for all API calls
TOKEN=<returned-token>
curl -X POST http://localhost:3000/api/items \
     -H "Content-Type: application/json" \
     -H "X-Workspace-Token: $TOKEN" \
     -d '{"url":"https://example.com"}'
curl http://localhost:3000/api/items -H "X-Workspace-Token: $TOKEN"
# 3. Admin: refresh all workspace feeds
curl -X POST http://localhost:3000/api/feeds/refresh-all \
     -H "X-Cron-Token: <cron-token>"

# Regen typed client after feature/DTO changes (preview.7+: SliceResult<T> typed)
dotnet build Inbox.slnx
dotnet tool run slicefx -- client csharp --project src/Inbox.Server \
  --namespace Inbox.Client --output src/Inbox.Client/SliceApiClient.g.cs --force
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

One-file-one-feature (SliceFx pattern). Route split: `spin-fileserver` at `/...` (SPA), `inbox-server` at `/api/...` (API). Same-origin — no CORS. **Multi-workspace**: each user has an anonymous workspace identified by an opaque server-issued token. Token entered in the SPA Login page; held in `sessionStorage` + in-memory `RefreshTokenHolder`; injected via `RefreshTokenHandler : DelegatingHandler` as `X-Workspace-Token`.

```
src/Inbox.Contracts/
  ItemContracts.cs       InboxItem, FeedSubscription, ItemStatus, Workspace (public)
  RequestContracts.cs    all boundary DTOs (form-backers: mutable; responses: positional)
                         + CreateWorkspaceResponse

src/Inbox.Client/        Blazor WASM SPA
  Program.cs             DI: named HttpClient + RefreshTokenHandler + RefreshTokenHolder + ISessionStorage
  SliceApiClient.g.cs    generated typed client (slicefx client csharp, preview.7+)
  RefreshTokenHandler.cs DelegatingHandler injecting X-Workspace-Token
  RefreshTokenHolder.cs  singleton in-memory token + sessionStorage hydration
  SessionStorage.cs      thin IJSRuntime wrapper over sessionStorage
  Layout/MainLayout.razor nav + route guard (redirects to /login when no token)
  Pages/Login.razor       /login — create workspace / paste token / try demo
  Pages/Items.razor       / — item list + add URL form + filter
  Pages/ItemDetail.razor  /items/{id} — single item (SPA deep-link target)
  Pages/Feeds.razor       /feeds — feed list + subscribe + manual refresh
  Pages/Settings.razor    /settings — token clear / logout
  Components/InboxItemCard.razor  item card with read/tag/delete actions

src/Inbox.Server/
  Features/Workspaces/CreateWorkspace.cs  POST /api/workspaces  (no auth; public self-registration)
  Features/Workspaces/EnsureDemo.cs       POST /api/demo  (no auth; idempotent demo seed)
  Features/Feeds/AddFeed.cs        POST /api/feeds        (X-Workspace-Token required)
  Features/Feeds/GetFeeds.cs       GET /api/feeds         (X-Workspace-Token required)
  Features/Feeds/RefreshFeeds.cs   POST /api/feeds/refresh  (X-Workspace-Token; caller workspace only)
  Features/Feeds/RefreshAllFeeds.cs  POST /api/feeds/refresh-all  (X-Cron-Token; all workspaces; admin)
  Features/Items/PostItem.cs       POST /api/items        (X-Workspace-Token required)
  Features/Items/GetItems.cs       GET /api/items         (?q= ?tag= ?status= filters; auth required)
  Features/Items/GetItem.cs        GET /api/items/{id}    (X-Workspace-Token required)
  Features/Items/UpdateItem.cs     PATCH /api/items/{id}  (X-Workspace-Token required)
  Features/Items/DeleteItem.cs     DELETE /api/items/{id} (X-Workspace-Token required)
  Infrastructure/IAuthenticator.cs       workspace token → wid resolution interface
  Infrastructure/KvAuthenticator.cs      IAuthenticator impl — KV lookup (token:{token} → wid)
  Infrastructure/WorkspaceKeys.cs        KV key construction (all formats in one place)
  Infrastructure/WorkspaceProvisioner.cs workspace creation + demo seeding
  Infrastructure/TokenAuth.cs            TokenAuth.SafeEquals — constant-time compare for admin cron_token
  Infrastructure/ITokenGuard.cs          (empty — kept for file-system compatibility; content removed)
  Infrastructure/FeedParser.cs           RSS/Atom feed parser
  Infrastructure/FeedRefreshCronHandler.cs  ISpinCronHandler impl → RefreshAllWorkspacesAsync
  Infrastructure/SpinKeyValueStore.cs    wasi:keyvalue WIT-bound IKeyValueStore impl
  Infrastructure/SpinVariables.cs        fermyon:spin/variables WIT-bound ISpinVariables impl
  Infrastructure/SpinWasiHttpClient.cs   wasi:http/outgoing-handler IWasiHttpClient impl
  IncomingHandlerImpl.cs             wasi:http/incoming-handler bridge
  CronHandlerBridge.cs               world-level handle-cron-event bridge
  InboxJsonContext.cs                source-gen JSON context
  spin.toml                          Local Spin manifest (cron trigger; cron_token/registration_open vars)
  spin.cloud.toml                    Fermyon Cloud manifest (HTTP only; same vars)
```

KV key scheme (per-workspace, multi-tenant):
- `token:{token}` → wid (string) — auth reverse-lookup
- `workspace:{wid}` → JSON `Workspace` — workspace metadata
- `workspaces:index` → JSON `string[]` — all wids (for cron orchestration)
- `w:{wid}:item:{id}` → JSON `InboxItem`
- `w:{wid}:items:index` → JSON `string[]` of item IDs (insertion order)
- `w:{wid}:feed:{id}` → JSON `FeedSubscription`
- `w:{wid}:feeds:index` → JSON `string[]` of feed IDs (insertion order)

Demo workspace: `wid="demo"`, token=`"demo-access-token"` (fixed/public). Shared read-write space for all visitors. Seeded with sample bookmarks by `POST /api/demo`.

Status vocabulary (`Inbox.Contracts.ItemStatus`): `unread` (default) / `read` / `archived`

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
    - OG title fetch implemented (best-effort, fail-open) in `PostItem.cs`. https redirects
      followed up to 3 hops (http:// not followed); UTF-8 decode only (non-UTF-8 pages may produce
      garbled titles — WASI runtime encoding support constraint).
    - **Multi-workspace**: all endpoints (including GET) now require X-Workspace-Token. Public
      read-only access is removed (was information-leak). Workspace tokens issued via POST /api/workspaces.
    - Tokens stored raw in KV (WASI has no crypto hashing). KV read access = all tokens exposed.
    - `Guid.NewGuid()` CSPRNG quality unconfirmed in WASI. Double-Guid token adds collision resistance
      but NOT prediction resistance if RNG is weak.
    - `workspaces:index` has a read-modify-write race on concurrent registration; lost registrations
      still authenticate fine but may be skipped by cron refresh.
    - Demo workspace (`wid=demo`) is shared read-write: all visitors get the same token, can mutate
      data, and can trigger server-side OG-fetch to arbitrary https URLs. Posture change from previous
      "all outbound is auth-gated" judgment. Mitigated by WASI sandbox + https-only outbound.
    - `registration_open` kill switch fails-open (unset/WIT-error → registration allowed).
      Hard cap at 1000 workspaces as additional guard.
    - Old global KV keys (`item:*`, `items:index`, etc.) abandoned; consume KV quota until manually wiped.
    - All 8 handlers now return `SliceResult<T>` or `SliceResult` (non-generic), resolved in
      slicefx#5 (preview.7). `SliceApiClient.g.cs` is fully generated; `SliceApiClient.cs`
      (hand-written) and `SliceApiClient.evidence.g.cs` (dogfood artifact) are removed.
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
Workspace auth is via `IAuthenticator` / `KvAuthenticator` — keyed KV lookup (O(1), no shared secret).
Admin auth (POST /api/feeds/refresh-all) uses `TokenAuth.SafeEquals` with the `cron_token` Spin variable.
`TokenAuth.SafeEquals` (constant-time comparison) lives in `Infrastructure/ITokenGuard.cs` (file retained, ITokenGuard interface removed).

### Spin variables binding shape

`fermyon:spin/variables@2.0.0` generates a free function on a `*Interop` static class:

```csharp
using VariablesInterop = ProxyWorld.wit.imports.fermyon.spin.v2_0_0.VariablesInterop;
// VariablesInterop.Get(name) returns string or throws WitException<IVariables.Error>
// IVariables holds only the Error type — it is NOT the call entry point.
```

`SliceFx.Wasi.Spin` (preview.5+) exposes `ISpinVariables` / `InMemorySpinVariables` as a
higher-level abstraction over this pattern (fail-closed, async surface over sync WIT).

### NativeAOT-LLVM WASI BCL gaps hit in this app

These are upstream toolchain gaps (NativeAOT-LLVM / componentize-dotnet), not SliceFx issues.
Each inline comment at the fix site explains the why; pointers below for navigation:

- `System.Security.Cryptography` unavailable — `Infrastructure/ITokenGuard.cs:17`
  (XOR loop pattern: see `docs/patterns/platform-abstraction.md` § "WASI implementation notes" in `~/dev/slicefx`)
- `MemoryExtensions.Contains<T>(ReadOnlySpan, T, IEqualityComparer)` ILC always-throw — `Features/Items/GetItems.cs:48`
- `TimeSpan.FromMilliseconds(long)` absent, causes `.cctor` throw — `Infrastructure/HtmlMetadataParser.cs:17`
- `HttpClient` async unusable (single-thread continuation model) — `Infrastructure/SpinWasiHttpClient.cs:103`, `InboxApp.cs:25`
- Non-UTF-8 response bodies: garbled decode, handled fail-open — `Infrastructure/HtmlMetadataParser.cs:11`

### Cron trigger wiring

- WIT export is world-level `handle-cron-event` via `IProxyWorld` (NOT `IIncomingHandler`).
- `async func` exports fail component encoding in componentize-dotnet 0.7.0-preview → use sync `func`.
- Cron expression: **6 fields** `{sec} {min} {hour} {dom} {month} {dow}` — 7 fields → ParseSchedule error.
- `spin.toml` uses `0 */1 * * * *` (every minute) for local testing convenience.
  Production refresh is driven by GitHub Actions (`*/30 * * * *`, every 30 min) calling
  `POST /api/feeds/refresh-all` with `X-Cron-Token` — Fermyon Cloud does not support cron natively.
  GitHub secret: `INBOX_CRON_TOKEN` (was `INBOX_REFRESH_TOKEN` — must be rotated at deploy).
  Cloud Spin variable: `cron_token` (was `refresh_token`).
