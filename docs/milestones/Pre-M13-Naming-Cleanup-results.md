# Pre-M13 Development naming cleanup results

Status: complete; ready for independent QA.

## Exact tested source

The implementation and verification source commit is `f5c839ed10e64e320812c9c36ca5c8fe4fec8e91` on `pre-m13-naming-cleanup`. A documentation-only evidence commit follows this result record.

## Renamed active contracts

- `M10Options` / `M10Options.cs` became `AppLaunchOptions` / `AppLaunchOptions.cs`; `PipeName` became `TestEventPipeName`.
- `M10PipeClient` / `M10PipeClient.cs` became `TestEventPipeClient` / `TestEventPipeClient.cs`.
- Startup arguments are now `--test-event-pipe`, `--diagnostics-path`, `--show-on-start`, and `--test-exit-after-visible-ready-count`; `--index` is unchanged. Legacy `--m08-*` and `--m10-*` aliases are not supported.
- The narrow worker uses `--internal-index-operation`, `--internal-operation-id`, `--internal-mount-point`, and `--internal-volume-identity` at both process-contract endpoints.
- `vm-verify.ps1` now exposes `ProtectedIndexRuntime`, semantic worker helpers, and `C:/Temp/Quail-Verify` defaults.

## Deliberate historical exclusions

Historical milestone documents/evidence, provenance regression-suite names, `spikes/m08`, `spikes/m09`, M10 harness metrics/output labels, and `publish-m10.ps1` plus icon scripts remain unchanged. They describe historical evidence or are deferred naturally to M13 deployment work.

## Verification

- Focused Release tests: 8 passed.
- Full Release test suite: 157 passed, 0 failed.
- Release builds: App `win-x64`, CLI, and the M10 harness passed with 0 warnings and 0 errors.
- Fresh self-contained publish: 533 files, 240083350 bytes; ZIP SHA-256 `FB2B83696F9B46BEEA56F0AA4F4F66E1BC675A1385CBB8DEBF0274FBBC733BDE`.
- Full Quail-Lab `ProtectedIndexRuntime` passed: Build/Refresh/zero-change Refresh, medium-integrity status/search, protected ACL and reparse defenses, quiescent-sidecar checks, exit-code transport, concurrency `0,13`, and worker cleanup.

## Known limitations

None introduced. The first runtime attempt exposed an invalid temporary account name produced by the mechanical rename; it was corrected to the neutral valid `qverify` prefix and the complete scenario was rerun successfully.
