# Verification Playbook

This document records reusable verification policy and settled testing facts for Quail. Its purpose is to keep verification proportionate, avoid repeatedly rediscovering known environment behavior, and minimize expensive test work that does not add new confidence.

Milestone-specific verification requirements still take precedence. When they do not explicitly require a broader campaign, use the smallest verification set that proves the changed behavior and protects the affected boundary.

## Core principle: test the change, not the entire history

Before running an expensive, repetitive, environment-sensitive, or long-running verification step, identify what changed since the last applicable PASS.

Reuse previous evidence for boundaries that have not materially changed. Do not repeat a full historical validation campaign merely because a new milestone started.

Re-run broader validation only when at least one of the following is true:

- the current change directly affects that boundary;
- a new observed regression implicates that boundary;
- a milestone explicitly requires the broader campaign;
- previous evidence is no longer applicable because the runtime, packaging, privilege, deployment, or relevant implementation path materially changed.

A new milestone number by itself is not sufficient justification.

## Settled findings

Treat documented, previously verified environment and product findings as settled unless new evidence gives a concrete reason to reopen them.

Do not spend time diagnosing a settled limitation again just to reconfirm it. Use the already established working path.

If a settled finding must be reopened, state the new evidence or changed boundary that invalidates the previous conclusion before starting a new investigation.

Infrastructure or harness behavior is not automatically a product defect. First distinguish among:

- product regression;
- test-fixture problem;
- automation limitation;
- environment/network/tooling failure;
- expected Windows behavior already documented by prior evidence.

Do not modify production code merely to satisfy an ambiguous harness observation without confirming the real user-visible behavior when that distinction matters.

## Verification depth

Use three practical levels of verification.

### Focused verification

Default after an ordinary implementation change.

Use targeted unit/integration tests, static inspection, one representative runtime path, or another narrow test that directly exercises the changed boundary.

Examples:

- a search projection change: focused search/result tests plus one representative Quick Search smoke;
- a UI binding change: focused UI/runtime smoke rather than a complete lifecycle campaign;
- a project-reference change: build plus payload/dependency inspection rather than repeated install/uninstall cycles.

### Representative regression verification

Use when a change crosses several connected components or affects a higher-risk boundary.

Test the directly affected functionality plus a small number of representative adjacent paths. Do not mechanically expand this into every historical test combination.

### Full campaign

Reserve full or high-iteration campaigns for milestones whose primary purpose requires them, release/stabilization gates, investigation of a concrete intermittent defect, or material changes to the boundary being stressed.

Examples include dedicated lifecycle/stress/performance milestones or a release candidate milestone that explicitly requires broad validation.

## Repetition and iteration counts

Do not use high iteration counts when a single or small-number smoke test is enough to establish correctness.

In particular:

- do not repeat dozens or hundreds of global-hotkey activations after an unrelated change;
- do not repeat large keyboard/lifecycle loops unless the current change affects hotkey registration, window lifecycle, focus/activation, input handling, process lifetime, or an observed intermittent defect requires repetition;
- do not increase iteration counts merely because an automated harness makes doing so easy.

When a high-iteration test is justified, explain what intermittent or cumulative failure it is intended to detect.

## Windows UI verification

For ordinary Windows UI and user-flow verification, prefer the shortest direct path that produces real behavioral evidence.

Use WinApp when available and suitable for direct interaction with the running Quail application. It is preferred for ordinary UI smoke such as:

- application startup and visible state;
- Quick Search interaction;
- keyboard navigation where supported;
- opening a selected result;
- Settings interaction;
- Index Manager interaction;
- representative Build/Rebuild/Refresh UI flow when the privilege-sensitive portion can still be verified appropriately.

Do not rediscover known limitations of indirect desktop automation before trying an already established working method.

The Hyper-V console is not the preferred automation input channel for Quail-Lab GUI testing when reliable interactive input is required. Use an established interactive-session method or WinApp instead when appropriate.

Existing SendInput/interactivity harnesses remain useful fallbacks and for focused cases that specifically require them. Do not automatically start every milestone with the historical M10 high-iteration harness.

## Quail-Lab

Use Quail-Lab when the test genuinely benefits from isolation or requires Windows boundaries that should not be exercised freely on the physical host.

Typical Quail-Lab responsibilities include:

- same-account UAC/elevated-worker behavior;
- protected `%PROGRAMDATA%` index storage;
- ACL/reparse/trust-boundary checks;
- Build/Rebuild/Refresh against a controlled disposable data volume;
- installation or deployment behavior when installation itself is affected;
- restart-sensitive or potentially destructive integration testing.

Do not use the VM as justification for repeating unrelated full-product campaigns.

Prefer one representative successful runtime path after focused automated verification unless the active milestone requires more.

## Physical host

Use the physical host only when the relevant requirement cannot be established adequately in Quail-Lab/WinApp, when the change affects host-specific integration, or when an environment-specific regression is observed.

Do not repeat a previous physical-host release-validation campaign for changes that do not affect the previously validated boundary.

## Build, publish, and installer verification

Treat build, publish, packaging, and installation as separate evidence levels.

A code or architecture change does not automatically require rebuilding and exercising the complete installer repeatedly.

Use the smallest applicable proof:

- normal source change: required project/solution build;
- production dependency or project-graph change: build/publish plus focused payload/dependency verification;
- packaging-script or installer-input change: build the affected package once and inspect/verify the affected payload;
- installer behavior change: representative installer runtime verification;
- release/stabilization milestone: broader installer/deployment campaign only when explicitly required.

For a new production assembly such as `Quail.FileSystem`, one successful final publish/package proof that the assembly and required runtime dependencies are present is sufficient unless the packaging logic itself is changing or a concrete failure requires another run.

Do not repeatedly rebuild the installer after no relevant input changed.

Do not run identical expensive packaging commands in a loop. After a failure, diagnose the cause first. Re-run only after a relevant correction or when needed to distinguish an infrastructure failure from a product/package failure.

Network/NuGet failures, restore outages, or other tooling failures should not trigger repeated full installer builds without a concrete hypothesis.

## Test infrastructure and harnesses

Prefer existing canonical scripts and established harnesses over creating new one-off infrastructure.

Do not create a new harness if WinApp, a focused existing test, a canonical script, or direct runtime inspection can prove the requirement with less work.

Do not turn temporary test infrastructure into production code.

When a harness produces a surprising result:

1. determine whether the fixture itself is valid;
2. compare with prior settled evidence;
3. verify real product behavior through the most direct available path when needed;
4. only then treat the observation as a product defect.

## Evidence reuse

Previous milestone evidence remains valid for unchanged behavior and boundaries.

Reference prior evidence rather than recreating it. New milestone evidence should record what was newly verified and why the selected verification was sufficient.

Do not copy large previous evidence sets into the current milestone.

## Stop wasting verification budget

Verification has a real time and model-budget cost. Once the active milestone's acceptance criteria are proven with appropriate evidence, stop testing.

Do not perform extra rounds of testing, hardening, or environment exploration simply to increase confidence abstractly.

Before starting another expensive verification step after the milestone already has substantial PASS evidence, ask:

1. What unverified requirement or concrete risk does this test address?
2. Did a relevant implementation input change since the last applicable PASS?
3. Would failure of this test materially change the milestone decision?

If the answers do not provide a concrete reason, do not run the test.
