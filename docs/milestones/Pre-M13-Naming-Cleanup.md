# Pre-M13 Development naming cleanup

## Goal

Remove accidental milestone-specific naming from the active Quail application runtime and current verification tooling without changing behavior.

## Scope

- Rename launch-option and test-event-pipe types and their private arguments.
- Rename the narrow elevated index-worker arguments at both ends of the process contract.
- Rename current protected-index verification scenario, helpers, generated names, and temporary root.
- Use neutral App source-link groups in the test project and update direct contract tests and the M10 harness invocation.

## Out of scope

UI polish, search/ranking behavior, index/schema/storage architecture, privilege model, installer/deployment/M13 work, dependencies, and historical milestone documentation/evidence.

## Semantic naming contract

The supported private startup arguments are `--index`, `--test-event-pipe`, `--diagnostics-path`, `--show-on-start`, and `--test-exit-after-visible-ready-count`. The elevated worker accepts only `--internal-index-operation`, `--internal-operation-id`, `--internal-mount-point`, and `--internal-volume-identity`.

## Verification

Run focused contract tests, the Release test suite, Release App/CLI/harness builds, and the complete `ProtectedIndexRuntime` Quail-Lab scenario using a fresh artifact. Audit active source and tooling for removed identifiers and review the final diff.

## Stop conditions

Stop if the canonical preparation fails, an old argument is a stable public contract, preserving behavior requires changing the privilege boundary or persistent/deployment architecture, or verification exposes an unrelated defect.
