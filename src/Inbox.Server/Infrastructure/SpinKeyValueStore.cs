// SPIKE_2: This file will contain the WIT-bound IKeyValueStore implementation for Spin/Fermyon Cloud.
//
// Steps to complete after spike 2 verification:
//
//   1. Determine WIT approach:
//      Option A — wasi:keyvalue/store@0.2.0-draft (WASI standard)
//                 Add to csproj: <Wit Include="wit/keyvalue.wasm" World="..." Registry="ghcr.io/webassembly/wasi/keyvalue:0.2.0-draft" />
//      Option B — fermyon:spin/key-value@2.0.0 (Spin native)
//                 Add to csproj: appropriate Spin WIT registry entry
//
//   2. Run: dotnet publish src/Inbox.Server -r wasi-wasm -c Release
//      Inspect the generated WIT bindings (look in obj/ for the generated C# files).
//
//   3. Implement SpinKeyValueStore below using the generated types.
//
//   4. In IncomingHandlerImpl.CreateApp(), replace:
//        builder.AddKeyValueStore(new InMemoryKeyValueStore());
//      with:
//        builder.AddKeyValueStore<SpinKeyValueStore>();
//
//   5. Add key_value_stores = ["default"] to spin.toml component section.

namespace Inbox.Server.Infrastructure;
