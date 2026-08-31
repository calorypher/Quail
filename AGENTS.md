# AGENTS.md

These instructions apply to the Quail repository unless a more specific file overrides them for a narrower scope.

## Canonical sources

This public repository, `calorypher/Quail`, is the canonical technical source for active Quail development.

Before starting a milestone, read at least:

- `README.md`;
- `ROADMAP.md`;
- this `AGENTS.md`;
- the active milestone specification;
- any architecture/methodology document explicitly referenced by that milestone.

Before planning or running Windows/runtime/package verification, also read `docs/verification-playbook.md`. Its settled findings and change-impact verification rules are canonical unless the active milestone explicitly requires stricter verification.

Chat history and Google Drive are not substitutes for repository state. The private historical archive is reference-only; do not use it as an active development repository unless a task explicitly requires historical investigation.

## Project intent

Quail is a Windows-first, local-first universal search system for the user's digital information. The current public implementation is deliberately file-first: Quail 0.2 provides a WinUI Quick Search desktop application backed by Quail's own local NTFS indexes, with GUI-managed index configuration and a diagnostic/administrative CLI.

The near-term product goal is to make filesystem search fast, relevant, continuously current, and comfortable enough for ordinary daily use before adding heterogeneous sources. Later sources should be added through validated vertical slices rather than through a speculative provider framework.

## Language and artifact conventions

- User-facing planning and implementation-agent prompts may be written in Polish.
- Repository artifacts must be written in English unless a narrower project document explicitly says otherwise. This includes source code, identifiers, file names, technical documentation, comments, commit messages, PR titles/bodies, test names, logs intended for durable repository evidence, and release notes.
- The application UI is English-first.
- Localization is a later product concern and should be introduced only when user-facing strings and the relevant UI are sufficiently stable. Do not introduce localization infrastructure without an explicit milestone requirement.

## Technical direction

Current default choices:

- C#;
- current stable .NET, with .NET 10 LTS as the current preferred baseline;
- WinUI 3 for the Windows application UI;
- SQLite for persistent local indexes where appropriate;
- official Windows APIs for NTFS/MFT/USN access where feasible;
- thin P/Invoke/interop rather than a custom NTFS parser;
- Windows-first implementation;
- no native C++/Rust component without demonstrated need;
- no dynamic or third-party plugin framework unless later evidence creates a real requirement.

The intended compile-time dependency direction is source-neutral and dependency-inverted:

```text
Quail.App ---------> Quail.Core
                         ^
                         |
Quail.FileSystem -------+

later:
Quail.Browser ----------+
Quail.Mail -------------+
Quail.Cloud ------------+
Quail.Calendar ---------+
```

`Quail.Core` owns application/search orchestration and only the minimal contracts genuinely shared by normal product surfaces. It must not reference `Quail.FileSystem` or another concrete source. Concrete sources implement Core-owned search contracts and depend on Core, not the reverse.

A small internal source contract such as `ISearchSource`, or an equally narrow mechanism, is acceptable when required for dependency inversion, search/result/action routing, and aggregation. Do not turn that seam into a public provider SDK, dynamic plugin loader, discovery/versioning system, capability framework, universal storage schema, or generalized provider lifecycle before multiple real source implementations demonstrate the common requirements.

At runtime, Core may orchestrate registered source instances. Runtime orchestration from App through Core to sources does not authorize a compile-time `Quail.Core -> Quail.FileSystem` dependency.

`Quail.App -> Quail.FileSystem` may exist where required for static composition or explicitly filesystem-specific administration/UAC UI, but those references must remain isolated from the normal Quick Search/search-coordinator/presentation path. Indexing, persistence, search implementation, ranking implementation, identity, synchronization/change tracking, and source-specific actions should not depend on WinUI.

Normal App/Core search-flow types and names should be source-neutral unless a component genuinely handles only one source. GUI-facing result models should expose only product information needed by the current surface and must not make filesystem concepts such as path, directory/file shape, NTFS attributes, or database identity mandatory for every future result.

Preserve source-native identity where available. Do not reduce filesystem identity to the current path merely to fit a generic model. Provider/source identity, content identity, and future cross-source relationships are separate concepts.

First-party source modules are intended to become physically optional over time. Omitting a source assembly should eventually remove that source's results, actions, indexing/synchronization behavior, and source-specific settings without breaking source-neutral search surfaces or Core. Do not implement runtime module loading before a concrete milestone requires it, but do not introduce dependencies that would make future physical optionality require another broad search-stack refactor.

## Resource and quality constraints

Lightweight operation is a first-class requirement.

Working targets for the complete application:

- approximately 100 MB working set or less when idle as a long-term aspiration;
- practically 0% idle CPU when no indexing or housekeeping work is required;
- no regular full filesystem rescans during healthy normal operation;
- no aggressive polling;
- fast startup;
- search that feels immediate to the user.

Measure before optimizing. Optimize measured bottlenecks and representative real workflows rather than synthetic scores alone.

Prefer straightforward, readable C# over clever language constructs when both approaches are adequate.

Keep source formatting reviewable:

- use normal idiomatic multiline C# for non-trivial methods and control flow;
- do not compress multiple statements, declarations, or non-trivial blocks onto one line merely to reduce file length;
- short expression-bodied members and simple single-line statements are fine when they remain immediately readable;
- formatting-only changes must remain semantically neutral and should not hide behavior changes in the same diff;
- before handoff, inspect changed C# files for accidental structural minification; whitespace formatting alone is not proof that the code is readable.

## Risk-based engineering proportionality

Apply engineering rigor according to consequence and likelihood. The goal is neither maximal hardening everywhere nor minimal verification everywhere.

High-impact boundaries warrant strong, and when appropriate adversarial, verification. Examples include:

- privilege boundaries, UAC/elevated workers, and Windows Service interfaces;
- ACLs, protected storage, reparse points, junctions, symlinks, and other paths by which an unelevated actor could influence privileged behavior;
- installer behavior that performs privileged operations;
- index integrity, crash consistency, and recovery semantics;
- correctness of NTFS/USN change tracking and loss-of-continuity handling;
- security-sensitive package/update authenticity mechanisms;
- measured search hot paths where performance is a primary product requirement.

Ordinary product workflows require solid representative testing, not exhaustive combinatorial hardening. Examples include normal settings behavior, launch-on-startup, tray/lifecycle behavior, ordinary install/uninstall flows, and routine UI interactions.

Do not add substantial complexity for unlikely low-impact legacy, deployment, or configuration scenarios unless a concrete requirement, observed defect, or credible high-impact risk justifies it. Development releases may deliberately require manual uninstall, index rebuild, or another simple recovery path when supporting every historical variant would add disproportionate complexity.

A high-severity finding may justify substantial hardening even when the triggering scenario is uncommon. A low-severity inconvenience does not justify substantial complexity merely because many theoretical variants can be enumerated.

Before materially escalating architecture, compatibility machinery, robustness layers, or verification scope, answer:

1. What concrete requirement, defect, measured limitation, or credible risk is being addressed?
2. Why is the simpler solution insufficient?
3. What ongoing implementation, testing, and maintenance cost does the extra complexity create?

If those answers are weak, defer the idea rather than implementing it.

## Architecture and scope control

Do only the active milestone.

Do not add features, refactors, frameworks, abstractions, packaging, telemetry, release machinery, or optimizations outside the milestone unless they are strictly required to satisfy its acceptance criteria or to fix a concrete in-scope defect or credible high-impact risk.

Do not improve robustness, generality, security, compatibility, deployment flexibility, or architecture beyond what the milestone requires merely because a more elaborate design can be imagined. Record worthwhile out-of-scope improvements in the handoff or roadmap instead.

Explicitly avoid early introduction of:

- dynamic or third-party plugin frameworks, plugin discovery/loading, public extension SDKs, and compatibility/versioning machinery;
- speculative provider capability matrices or generalized provider lifecycle APIs;
- content indexing before an approved content-search slice;
- AI features;
- cloud/network indexing before an approved source slice;
- speculative cross-platform abstractions;
- custom NTFS parsing;
- native C++/Rust components without demonstrated need;
- elaborate installer/update systems without a dedicated requirement;
- Windows Service responsibilities beyond the smallest justified privileged/background boundary.

Internal source/provider boundaries are not prohibited by this rule. Extract them when current code and an approved product slice demonstrate the need. A minimal Core-owned source-search contract needed to keep Core independent of concrete sources is an internal boundary, not a speculative provider framework.

Stop when the approved acceptance criteria are met. Do not add extra rounds of hardening, compatibility work, cleanup, or generalization after PASS unless verification has found a new concrete defect or risk that belongs to the milestone.

## Packaging and update direction

Quail 0.2 uses Inno Setup and a fixed per-machine installation under `C:\Program Files\Quail`. User settings and index data live outside the application directory. The 0.2 development-release contract does not promise compatible in-place upgrade across every historical development build or installation variant.

Do not recreate broad legacy cleanup or upgrade machinery without a new product requirement. A documented manual uninstall/reinstall or index rebuild is acceptable for an unusual development-release transition when it is materially simpler and does not risk user-owned data.

Automatic update installation is not currently implemented. A future no-prompt background update experience may be evaluated as its own security-sensitive milestone. Compare mature deployment/update options against the actual requirements at that time rather than assuming the current installer must remain permanent.

Do not broaden a privileged filesystem service into a generic updater merely to remove UAC. Any privileged updater design requires narrow responsibilities, strong package authenticity verification, constrained update sources, safe replacement/recovery behavior, and independent security review proportional to that boundary.

## Reusable Windows lab

Quail has a reusable Hyper-V VM named `Quail-Lab` for elevated, destructive, restart-sensitive, and Windows-integration testing. It is a project-level lab, not a milestone-specific VM.

General rules:

- milestone-specific known-good states are represented by Hyper-V checkpoints such as `M01-clean`, `M02-clean`, and so on;
- the VM may be accessed from the physical host over SSH using the local administrator account configured for the lab;
- SSH login starts in `cmd.exe`; commands using PowerShell cmdlets must explicitly invoke `powershell -NoProfile` or an equivalent PowerShell host;
- canonical lab scripts start only an `Off` VM, otherwise continue with a `Running` VM, and use a bounded dynamic-IP/SSH wait; do not stop or reset a running lab merely to normalize it;
- canonical SSH/SCP uses the stable `HostKeyAlias` for the VM and the ignored `.quail-tooling` trust store: an unchanged key follows a dynamic IP without a prompt, while a changed key is a hard failure requiring investigation;
- the VM has its own repository clone and should not rely on writable shares containing important host data;
- do not attach physical host disks as pass-through disks;
- destructive storage experiments should use a disposable virtual data disk when possible;
- unrestricted administrative experimentation is confined to the VM;
- the physical host is not an unrestricted administrative sandbox;
- milestone specifications may define stricter lab rules and always take precedence for that milestone.

Do not assume a stable VM IP address. Discover the current address when needed.

## Milestone workflow

Each implementation milestone should define:

- goal;
- scope;
- out of scope;
- acceptance criteria;
- verification method;
- stop conditions.

At the start of an implementation-agent milestone:

1. Check the current branch, exact HEAD, remotes, and worktree state.
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
- use strong verification where the actual risk justifies it and representative verification elsewhere;
- do not ask the user to decide normal implementation details that follow from the specification;
- stop rather than improvising when requirements conflict, required hardware/data is unavailable, or a material scope expansion would be necessary.

The normal completion boundary is a verified branch plus PR ready for independent project/QA review, unless the milestone explicitly defines another boundary.

At final handoff, note newly observed repeatable, deterministic, materially useful procedures that may be scriptable. Do not implement that tooling automatically outside the active milestone; add it only when repetition and evidence justify its maintenance cost.

## Verification and handoff

A successful build alone is not completion.

Verification is change-impact and risk based. Reuse previous PASS evidence for unchanged boundaries. Do not repeat full hotkey, lifecycle, installer, deployment, physical-host, or other historical campaigns unless the current change affects that boundary, a new regression implicates it, or the active milestone explicitly requires it. Expensive repeated verification must have a concrete unanswered requirement or risk; otherwise stop once sufficient PASS evidence exists. Follow `docs/verification-playbook.md`.

Verify the actual result with the appropriate combination of:

- automated tests;
- release/debug builds as required;
- CLI/runtime smoke tests;
- real Windows/NTFS behavior;
- security/adversarial review where a genuine privileged or trust boundary warrants it;
- resource/performance measurements when required by the milestone;
- durable evidence recorded in the repository or PR.

Avoid verification matrices whose size is driven mainly by hypothetical combinations rather than risk or representative supported workflows. Do not use high iteration counts simply because an automated harness makes them cheap to invoke. Do not rebuild or reinstall an unchanged package repeatedly without a relevant input change or concrete diagnostic hypothesis.

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
- creating or moving release tags;
- publishing a GitHub Release;
- replacing published release assets;
- destructive actions outside the repository.

Approval to merge does not imply approval to delete the branch, create tags, or publish a release.

If unrelated local changes are present, stop. Do not stash, clean, reset, or otherwise hide them without explicit instruction.

Released tags are immutable baselines. Documentation-only work after a release belongs on newer commits and does not move an existing release tag.

## Public release and naming

Update `CHANGELOG.md` for significant user-visible changes intended for the next release. It is not a commit log; milestone evidence remains under `docs/milestones/`, and development-only defects fixed before release do not need separate entries unless they describe a change from an already released version.

The active repository is public. Quail source code is released under the MIT License. The code license does not define rights to the Quail name, logo, or other branding assets; treat branding separately.

The final 0.2 naming gate was **KEEP WITH CAUTION**. A known caution is the pending Polish word mark `Quail Digital`, application Z.601622, class 9, which includes broad computer-software wording. This is not legal clearance and was not considered a blocker for the current free hobby FOSS project.

Do not repeat a full naming/trademark research exercise for ordinary milestones. Revisit professional clearance if Quail becomes paid/SaaS, the brand acquires material business value, the product moves materially toward business/cloud-data services, or a new concrete conflict appears.