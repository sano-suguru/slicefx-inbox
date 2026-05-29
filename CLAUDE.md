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

- **A** ✅: API only (POST/GET/DELETE items, KV store)
- **A.5**: Blazor UI + `spin-fileserver` route split (SPA on `/`, API on `/api/*`)
- **B** ✅: RSS feeds + `SliceFx.Wasi.Spin` satellite (cron trigger) + GH Actions scheduler + auth (Spin variables)
- **C**: Search, tags, read/archive management
- **D**: (merged into B) Bearer auth + Spin variables — done
- **E**: Polish + v1 readiness assessment

---

## SliceFx preview.5 フィードバック候補（実測、2026-05-30）

Increment B spike で判明した SliceFx framework 側の観察。本体 (`~/dev/slicefx`) は spike が
abstraction gap を示すまで触らない方針のため、ここに記録しておく。

### 1. `SpinCronContext.Metadata` が常に null

`spin:cron@3.0.0` WIT の `metadata` は `{ timestamp: u64 }` のみ。`SpinCronContext.cs` の
`Metadata: string?` プロパティに WIT 上の source が存在しない。YAGNI で削除するか将来の
trigger metadata 拡張余地として残すかは preview.5 で判断。

### 2. world-level export の async func は component encoding で失敗

`async func` WIT export → componentize-dotnet 0.7.0-preview が `[async]handle-cron-event` を
WASM export name として生成するが、`wasm-tools` は `handle-cron-event` を期待する（async ABI gap）。  
**現状の正解**: `func`（sync）export。Spin trigger-cron 0.5.0 は sync 実装で問題なく動作する。  
`IProxyWorld.static abstract void HandleCronEvent(...)` = world-level export（interface export の
`IIncomingHandler` とは別物）。SliceFx.Wasi.Spin の docs/sample に記録推奨。

### 3. cron expression は 6 フィールド

Spin trigger-cron 0.5.0 は **6 フィールド** `{sec} {min} {hour} {dom} {month} {dow}` のみ受理。
7 フィールド（quartz 形式）は "ParseSchedule" エラー。`SpinCronContext.cs` のコメントまたは
README に注記推奨。

### 4. `[FromHeader]` 位置引数は param 名にバインドされる（罠）

`[FromHeader("X-Refresh-Token")]`（位置引数）は source generator が `Name=` 名前付き引数しか
読まない（`SliceFeatureGenerator.cs:954-961`）ため、位置引数は無視され param 名のヘッダーに
バインドされる（silent mismatch）。正しくは `[FromHeader(Name = "X-Refresh-Token")]`。  
診断（SLICE0xx）の追加またはドキュメント明記が候補。本 repo で実装・実証済み。

### 5. SliceFx.Wasi に Spin variables サポートなし（要 raw WIT）

`fermyon:spin/variables@2.0.0` は `SliceFx.Wasi.*` パッケージに抽象化されていない。
利用するには `combined.wit` に inline WIT 定義を追加し、生成 binding を直接呼ぶ必要がある
（本 repo では `SpinVariables.cs` として実装）。`SliceFx.Wasi.Spin` か新 satellite に
`ISpinVariables` 抽象と実装を追加することを preview.5 の候補として記録。
