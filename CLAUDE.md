# CLAUDE.md — slicefx-inbox

## What this is

Personal read-later + RSS inbox app (multi-workspace), dogfooding [SliceFx](https://github.com/sano-suguru/slicefx) on Fermyon Cloud (Spin WASI).

Deploy target: Fermyon Cloud free tier (5 apps / 100K req/mo / 100 MB component limit).
Backend: `src/Inbox.Server/` — SliceFx WASI component.
Frontend: `src/Inbox.Client/` — Blazor WASM SPA.

## SliceFx reference rules

- **NuGet only — `<ProjectReference>` to slicefx/ is prohibited** (pack-path bug detection).
- When you find a SliceFx bug: switch to `~/dev/slicefx` session → fix → `gh workflow run publish.yml` (or local feed push) → bump `<PackageReference Version>` here.
- Local NuGet feed (`dotnet pack && dotnet nuget push --source <local>`) is available as an escape hatch if the publish→version-bump roundtrip becomes a bottleneck.
- Paired plan file (slicefx main session): `~/.claude/plans/slicefx-concurrent-deer.md`

## Commands

```bash
# Regular build (non-WASI, no WIT bindings needed)
dotnet build Inbox.slnx

# WASI publish — requires linux-x64 or win-x64 host; on macOS use Docker linux/amd64
dotnet publish src/Inbox.Server -r wasi-wasm -c Release
# Copies output to src/Inbox.Server/dist/inbox-server.wasm
# DWARF strip runs automatically via the StripWasiDebugSections MSBuild target (49MB → ~25MB)
# if wasm-tools is on PATH (brew install wasm-tools); warning only if absent.
# NOTE: Docker-based publish (mcr.microsoft.com/dotnet/sdk:10.0) does NOT include wasm-tools —
# the auto-strip is skipped and the output remains ~49MB. Fermyon Cloud free tier limit is 100MB
# so 49MB is safe for now; if size grows, strip manually on the host after Docker publish:
#   wasm-tools strip -o src/Inbox.Server/dist/inbox-server.wasm src/Inbox.Server/dist/inbox-server.wasm

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

# Regen typed client after feature/DTO changes
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

One-file-one-feature (SliceFx pattern). Route split: `spin-fileserver` at `/...` (SPA), `inbox-server` at `/api/...` (API) and `/s/...` (public share pages). Same-origin — no CORS. **Multi-workspace**: each user has an anonymous workspace identified by an opaque server-issued token. Token entered in the SPA Login page; held in `sessionStorage` + in-memory `RefreshTokenHolder`; injected via `RefreshTokenHandler : DelegatingHandler` as `X-Workspace-Token`.

```
src/Inbox.Contracts/
  ItemContracts.cs       InboxItem, FeedSubscription, ItemStatus, Workspace (public)
  RequestContracts.cs    all boundary DTOs (form-backers: mutable; responses: positional)
                         + CreateWorkspaceResponse

src/Inbox.Client/        Blazor WASM SPA
  Program.cs             DI: named HttpClient + RefreshTokenHandler + RefreshTokenHolder + ISessionStorage
  SliceApiClient.g.cs    generated typed client (slicefx client csharp)
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
  Features/Share/CreateShare.cs    POST /api/items/{id}/share    (X-Workspace-Token; idempotent)
  Features/Share/RevokeShare.cs    DELETE /api/items/{id}/share  (X-Workspace-Token; idempotent)
  Features/Share/GetSharePage.cs   GET /s/{token}  (no auth; public server-rendered OGP HTML)
  Infrastructure/IAuthenticator.cs       workspace token → wid resolution interface
  Infrastructure/KvAuthenticator.cs      IAuthenticator impl — KV lookup (token:{token} → wid)
  Infrastructure/WorkspaceKeys.cs        KV key construction (all formats in one place)
  Infrastructure/WorkspaceProvisioner.cs workspace creation + demo seeding
  Infrastructure/ITokenGuard.cs          TokenAuth.SafeEquals — constant-time compare for admin cron_token
  Infrastructure/FeedParser.cs           RSS/Atom feed parser
  Infrastructure/FeedRefreshCronHandler.cs  ISpinCronHandler impl → RefreshAllWorkspacesAsync
  Infrastructure/SpinKeyValueStore.cs    wasi:keyvalue WIT-bound IKeyValueStore impl
  Infrastructure/SpinVariables.cs        fermyon:spin/variables WIT-bound ISpinVariables impl
  Infrastructure/SpinWasiHttpClient.cs   wasi:http/outgoing-handler IWasiHttpClient impl
  Infrastructure/HtmlPage.cs             server-generated HTML + XSS escape boundary (share/404 pages)
  IncomingHandlerImpl.cs             wasi:http/incoming-handler bridge
  CronHandlerBridge.cs               world-level handle-cron-event bridge
  InboxJsonContext.cs                source-gen JSON context
  spin.toml                          Local Spin manifest (cron trigger; cron_token/registration_open/public_base_url vars; /s/... trigger)
  spin.cloud.toml                    Fermyon Cloud manifest (HTTP only; same vars incl. public_base_url)
```

### KV key scheme (per-workspace, multi-tenant)

- `token:{token}` → wid (string) — auth reverse-lookup
- `workspace:{wid}` → JSON `Workspace` — workspace metadata
- `w:{wid}:item:{id}` → JSON `InboxItem`
- `w:{wid}:feed:{id}` → JSON `FeedSubscription`
- `share:{shareToken}` → `"{wid}:{itemId}"` — public reverse-lookup (presence = publicly readable)
- `w:{wid}:share:{id}` → shareToken — forward lookup (idempotent create + DeleteItem/revoke cleanup)
  - **Prefix note**: forward share key is placed under `w:{wid}:share:` (not `w:{wid}:item:`) to
    avoid matching `ItemPrefix(wid)` scans — mixing it under `:item:` would cause
    `CountItemKeysAsync` to double-count share keys and `ListItemsAsync` to attempt
    (and fail) deserialising them as JSON.

Listings (items, feeds, workspaces) use `get-keys` prefix scan (`KvScan.cs`) — single-key writes eliminate the read-modify-write race of mutable index keys.
Prefix constants: `WorkspaceKeys.WorkspacePrefix`, `ItemPrefix(wid)`, `FeedPrefix(wid)`.
Performance: O(total keys) per list call (acceptable at dogfood scale; note in CLAUDE.md if key count grows materially).

## Auth model

- **Workspace token**: opaque server-issued token from `POST /api/workspaces`. Stored in `sessionStorage`; injected as `X-Workspace-Token` by `RefreshTokenHandler`. All endpoints except workspace creation and public share pages require it.
- **Demo workspace**: `wid="demo"`, token=`"demo-access-token"` (fixed/public). Shared read-write space for all visitors. Feed subscriptions blocked (403) to prevent anonymous server-side-fetch amplification.
- **Admin cron token**: `TokenAuth.SafeEquals` in `Infrastructure/ITokenGuard.cs` — constant-time XOR loop (crypto is unavailable in NativeAOT-LLVM WASI; `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` cannot be used).
- `registration_open` Spin variable **fails-closed**: unset / WIT error → 403. Explicit `"true"` required. Default in `spin.toml` is `"true"`. Hard cap at 1000 workspaces.

## Permanent constraints

These are accepted design constraints, not blockers:

- Tokens stored raw in KV (WASI has no crypto hashing). KV read access = all tokens exposed.
- `Guid.NewGuid()` CSPRNG quality unconfirmed in WASI. Double-Guid token adds collision resistance but NOT prediction resistance if RNG is weak.
- Per-workspace resource limits: `MaxFeedsPerWorkspace=50` (AddFeed returns 429), `MaxItemsPerWorkspace=2000` (RefreshFeeds skips workspace), `MaxEntriesPerRefresh=100` (per-feed cap per refresh sweep).
- OG title fetch in `PostItem.cs` is best-effort / fail-open. https redirects followed up to 3 hops (http:// not followed); UTF-8 decode only — non-UTF-8 pages may produce garbled titles.
- Old global KV keys (`item:*`, `items:index`, `feeds:index`, `workspaces:index`, etc.) are abandoned on the live deployment and consume quota until manually wiped.

---

## WASI / NativeAOT-LLVM gotchas

These are upstream toolchain gaps (NativeAOT-LLVM / componentize-dotnet), not SliceFx issues.

### NativeAOT-LLVM BCL gaps in this app

- `System.Security.Cryptography` unavailable → XOR loop pattern for constant-time compare (`Infrastructure/ITokenGuard.cs`)
- `MemoryExtensions.Contains<T>(ReadOnlySpan, T, IEqualityComparer)` ILC always-throw → `Features/Items/GetItems.cs:48`
- `TimeSpan.FromMilliseconds(long)` absent → use `double` literal (`Infrastructure/HtmlMetadataParser.cs:17`)
- `HttpClient` async unusable (single-thread continuation model) → `Infrastructure/SpinWasiHttpClient.cs:103`, `InboxApp.cs:25`
- Non-UTF-8 response bodies: garbled decode, handled fail-open → `Infrastructure/HtmlMetadataParser.cs:11`

### Spin variables binding shape

`fermyon:spin/variables@2.0.0` generates a free function on a `*Interop` static class:

```csharp
using VariablesInterop = ProxyWorld.wit.imports.fermyon.spin.v2_0_0.VariablesInterop;
// VariablesInterop.Get(name) returns string or throws WitException<IVariables.Error>
// IVariables holds only the Error type — it is NOT the call entry point.
```

`SliceFx.Wasi.Spin` exposes `ISpinVariables` / `InMemorySpinVariables` as a higher-level abstraction over this pattern (fail-closed, async surface over sync WIT).

### Cron trigger wiring

- WIT export is world-level `handle-cron-event` via `IProxyWorld` (NOT `IIncomingHandler`).
- `async func` exports fail component encoding in componentize-dotnet 0.7.0-preview → use sync `func`.
- Cron expression: **6 fields** `{sec} {min} {hour} {dom} {month} {dow}` — 7 fields → ParseSchedule error.
- `spin.toml` uses `0 */1 * * * *` (every minute) for local testing convenience.
  Production refresh is driven by GitHub Actions (`*/30 * * * *`, every 30 min) calling
  `POST /api/feeds/refresh-all` with `X-Cron-Token` — Fermyon Cloud does not support cron natively.
  GitHub secret: `INBOX_CRON_TOKEN`. Cloud Spin variable: `cron_token`.
