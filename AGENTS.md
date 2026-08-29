# AGENTS.md

These instructions apply to the Quail repository unless a more specific file overrides them for a narrower scope.

## Canonical sources

Once implementation begins, this repository is the canonical technical source.

Before starting a milestone, read at least:

- `README.md`;
- `ROADMAP.md`;
- this `AGENTS.md`;
- the active milestone specification;
- any architecture/methodology document explicitly referenced by that milestone.

Chat history and Google Drive are not substitutes for repository state.

## Project intent

Quail is a Windows-first, local-first universal search system for the user's digital information. The current implementation is deliberately file-first: it starts with a fast, lightweight Windows launcher/search experience backed by Quail's own local NTFS index, while later heterogeneous sources should be added only through validated vertical slices.

The first release focuses on a correct, fast Windows 11 x64 NTFS file engine and CLI. GUI and additional providers come later.

## Language and artifact conventions

- User-facing planning and implementation-agent prompts may be written in Polish.
- Repository artifacts must be written in English unless a narrower project document explicitly says otherwise. This includes source code, identifiers, file names, technical documentation, comments, commit messages, PR titles/bodies, test names, logs intended for durable repository evidence, and release notes.
- The application UI starts in English.
- Localization is a later product concern and should be introduced only when the UI exists and its user-facing strings are sufficiently stable. Do not introduce localization infrastructure into early milestones without an explicit milestone requirement.

## Technical direction

Current default choices:

- C#;
- current stable .NET, with .NET 10 LTS as the current preferred baseline;
- SQLite;
- official Windows APIs for NTFS/MFT/USN access where feasible;
- thin P/Invoke/interop rather than a custom NTFS parser;
- Windows-only v1;
- no native C++/Rust component without demonstrated need;
- search capabilities should evolve as clean internal providers/modules behind simple shared query/result contracts where appropriate;
- do not build a dynamic or third-party plugin framework in early milestones.

The exact persistent schema, search implementation, privilege model, and Windows Service boundary are not frozen before the relevant feasibility work.

A Windows Service is expected to be the likely privileged boundary for NTFS/MFT/USN access so the interactive launcher can remain unelevated. Do not run the final launcher UI elevated merely to read filesystem internals. M01 must validate the actual privilege requirements and determine the smallest sensible service responsibility.

## Resource and quality constraints

Lightweight operation is a first-class requirement.

Working targets for the complete application:

- approximately 100 MB working set or less when idle;
- practically 0% idle CPU when no indexing or housekeeping work is required;
- no regular full filesystem rescans;
- no aggressive polling;
- fast startup;
- search that feels immediate to the user.

Measure before optimizing. Do not add complexity merely to win synthetic benchmarks.

Prefer straightforward, readable C# over clever language constructs when both approaches are adequate.

Keep source formatting reviewable:

- use normal idiomatic multiline C# for non-trivial methods and control flow;
- do not compress multiple statements, declarations, or non-trivial blocks onto one line merely to reduce file length;
- short expression-bodied members and simple single-line statements are fine when they remain immediately readable;
- formatting-only changes must remain semantically neutral and should not hide behavior changes in the same diff;
- before handoff, inspect changed C# files for accidental structural minification; whitespace formatting alone is not proof that the code is readable.

## Architecture constraints

Keep the first implementation simple.

Prefer a small number of clear modules over speculative abstraction layers. Keep Windows/NTFS-specific code isolated where natural, but do not build a cross-platform filesystem abstraction for a hypothetical Linux port.

The long-term search architecture should support distinct internal providers/modules such as file search, application discovery, browser data, calculator/conversions, and web actions, with a common result/query layer where that separation provides clear value. This modularity is an internal architecture goal and does not imply a public or dynamically loaded plugin system.

The GUI must eventually remain thin: indexing, persistence, search, ranking, and provider logic should not depend on WinUI or WPF.

For filesystem namespace state, favor representations that can handle directory rename/move without rewriting an entire subtree. Do not freeze a schema before M01 establishes what identifiers and metadata are reliably available.

For incremental persistence, preserve crash consistency between index mutations and the USN checkpoint. The exact mechanism belongs to the relevant milestone.

## Packaging and update direction

Do not build custom packaging or update infrastructure while the service/application layout is still unstable.

When packaging becomes an active milestone, prefer a mature Windows installer. The current preferred direction is Inno Setup producing a single setup executable that can install, upgrade, and uninstall the complete Quail layout, including Windows Service registration when required.

For future updates, prefer reusing the normal installer rather than implementing a custom binary patcher or privileged file-replacement engine. A launcher-side updater may check for a newer release, download and verify the installer, then invoke it in silent/very-silent mode as appropriate.

Silent installer UI is not permission bypass. If an update modifies a per-machine installation or Windows Service, normal Windows elevation may still require UAC consent.

Do not broaden the privileged NTFS service into a generic updater merely to eliminate a UAC prompt. A future no-prompt background update mechanism is allowed for evaluation only as an explicit security-sensitive milestone with a narrowly scoped privileged design, strong package authenticity verification, constrained update sources, safe component replacement, failure handling, and independent review.

## Reusable Windows lab

Quail has a reusable Hyper-V VM named `Quail-Lab` for elevated, destructive, restart-sensitive, and Windows-integration testing. It is a project-level lab, not a milestone-specific VM.

General rules:

- milestone-specific known-good states are represented by Hyper-V checkpoints such as `M01-clean`, `M02-clean`, and so on;
- the VM may be accessed from the physical host over SSH using the local administrator account configured for the lab;
- SSH login starts in `cmd.exe`; commands using PowerShell cmdlets must explicitly invoke `powershell -NoProfile` (or an equivalent PowerShell host);
- canonical lab scripts start only an `Off` VM, otherwise continue with a `Running` VM, and use a bounded dynamic-IP/SSH wait; do not stop or reset a running lab merely to normalize it;
- canonical SSH/SCP uses the stable `HostKeyAlias` for the VM and the ignored `.quail-tooling` trust store: an unchanged key follows a dynamic IP without a prompt, while a changed key is a hard failure requiring investigation;
- the VM has its own repository clone and should not rely on writable shares containing important host data;
- do not attach physical host disks as pass-through disks;
- destructive storage experiments should use a disposable virtual data disk when possible;
- unrestricted administrative experimentation is confined to the VM;
- the physical host is not an unrestricted administrative sandbox;
- milestone specifications may define stricter lab rules and always take precedence for that milestone.

Do not assume a stable VM IP address. Discover the current address when needed.

## Scope control

Do only the active milestone.

Do not add features, refactors, frameworks, abstractions, packaging, telemetry, release machinery, or optimizations outside the milestone unless they are strictly required to satisfy its acceptance criteria.

When a useful improvement is outside scope, record it in the handoff or appropriate roadmap document instead of implementing it.

Explicitly avoid early introduction of:

- dynamic or third-party plugin frameworks, plugin discovery/loading, public extension SDKs, and compatibility/versioning machinery;
- content indexing;
- AI features;
- cloud/network indexing;
- cross-platform abstractions;
- custom NTFS parsing;
- native C++/Rust components;
- elaborate installer/update systems;
- Windows Service responsibilities beyond what privilege separation actually requires.

Internal provider/module boundaries are not prohibited by this rule; introduce them when they naturally support the active product slice without speculative framework complexity.

## Milestone workflow

Each implementation milestone should define:

- goal;
- scope;
- out of scope;
- acceptance criteria;
- verification method;
- stop conditions.

At the start of an implementation-agent milestone:

1. Check the current branch, exact HEAD, and worktree state.
2. Stop if there are unrelated local changes or a conflict with the milestone/source documents.
3. Read the canonical sources.
4. Work autonomously within the approved scope.

When access is available, the implementation agent is the default owner of routine non-destructive Git lifecycle work: safe fetch/pull, branch creation, clean-state checks, commits, pushes, and PR creation. Do not ask the user to run those routine Git commands when the agent can safely perform them. The implementation agent should likewise perform routine non-destructive host/VM milestone preparation when access is available.

Use a documented canonical repository script for a supported procedure instead of manually reconstructing an equivalent sequence. The narrow exceptions are debugging that script itself and a required case outside its documented contract. Existing safety gates remain unchanged.

`scripts/prepare-milestone.ps1` is the canonical supported path for its clean host/VM main, same-HEAD, data-volume, checkpoint, and branch-preparation contract. It may create one new uniquely named milestone checkpoint only after all documented guards pass. Existing checkpoint restore, deletion, rename, and overwrite remain explicitly safety-gated.

During implementation:

- make routine technical decisions autonomously when the requirements are clear;
- add/update tests needed to verify the real behavior;
- fix defects found within milestone scope;
- do not ask the user to decide normal implementation details that follow from the specification;
- stop rather than improvising when requirements conflict, required hardware/data is unavailable, or a material scope expansion would be necessary.

The normal completion boundary is a verified branch plus PR ready for independent project/QA review, unless the milestone explicitly defines another boundary.

At final handoff, note newly observed repeatable, deterministic, materially useful procedures that may be scriptable. Do not implement that tooling automatically outside the active milestone; add it only when repetition and evidence justify its maintenance cost.

## Verification and handoff

A successful build alone is not completion.

Verify the actual result with the appropriate combination of:

- automated tests;
- release/debug builds as required;
- CLI/runtime smoke tests;
- real Windows/NTFS behavior;
- resource/performance measurements when required by the milestone;
- durable evidence recorded in the repository or PR.

Handoff must include at least:

- exact branch and HEAD;
- worktree status;
- concise implementation summary;
- commands/tests executed and outcomes;
- relevant runtime/manual evidence;
- known limitations or deviations;
- PR link/number when applicable.

## Git and safety gates

Do not perform destructive or publication-sensitive actions without explicit user approval.

Separate approval is required for at least:

- merging a specific PR;
- deleting branches;
- force-push;
- destructive reset/clean operations;
- rebasing a published branch in a history-rewriting way;
- creating/moving release tags;
- publishing a GitHub Release;
- replacing published release assets;
- destructive actions outside the repository.

Approval to merge does not imply approval to delete the branch, create tags, or publish a release.

If unrelated local changes are present, stop. Do not stash, clean, reset, or otherwise hide them without explicit instruction.

## Public release and naming

Update `CHANGELOG.md` for significant user-visible changes intended for the next release. It is not a commit log; milestone evidence remains under `docs/milestones/`, and development-only defects fixed before release do not need separate entries unless they describe a change from an already released version.

The repository is currently private. The approved project name for a future public release remains **Quail**, and the approved public code license is **MIT**. The code license does not define rights to the Quail name, logo, or other branding assets; treat branding separately.

Do not repeat a full naming/trademark research exercise for ordinary milestones. Before changing the repository from private to public, perform a final manual check for `QUAIL`, `Q.QUAIL`, and materially similar software marks in the relevant EU/Poland sources, including TMview/EUIPO and UPRP. Reopen the naming decision only if that check finds a material conflict or the product later changes substantially toward hosted/SaaS search or business/cloud-data services.

Before public release, complete a dedicated readiness audit of the actual release candidate, including dependency/license compatibility, redistributed assets and notices, publish/installer payload licensing, repository history for secrets/private material, the standard MIT `LICENSE` text, required third-party notices, and public repository metadata. Do not add public-release infrastructure early merely because the naming and license decisions are known.
