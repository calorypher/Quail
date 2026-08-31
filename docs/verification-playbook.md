# Verification Playbook

This document records reusable verification policy and settled testing facts for Quail. Its purpose is to keep verification proportionate, reuse valid evidence, and avoid spending implementation time or model budget on tests that do not materially change confidence.

Milestone-specific verification requirements still take precedence. Use the smallest verification set that proves the changed behavior and protects the affected boundary.

An acceptance criterion that includes an ordinary manual UI smoke defines evidence needed before final milestone closure; it does not by itself assign that smoke to the implementation agent. Unless a milestone explicitly says otherwise, ordinary manual UI/UX smoke may be user-owned and may remain pending at the implementation-agent handoff.

## Core principle: test the change, not the entire history

Before an expensive, repetitive, environment-sensitive, or long-running verification step, identify what changed since the last applicable PASS.

Reuse previous PASS evidence for boundaries that have not materially changed. Re-run broader validation only when:

- the current change directly affects that boundary;
- a new observed regression implicates it;
- the active milestone explicitly requires the broader campaign;
- previous evidence is no longer applicable because the relevant runtime, packaging, privilege, deployment, or implementation path materially changed.

A new milestone number, branch, PR, Codex session, or context reset by itself is not justification for repeating an old campaign.

## Verification ownership hierarchy

Prefer verification in this order unless the active risk requires otherwise:

1. an existing deterministic repository test, script, benchmark, or harness;
2. a short user-owned manual check for directly observable behavior;
3. new agent-driven investigation or automation only when the first two cannot provide reliable evidence.

The implementation agent should spend its budget primarily on implementation, focused automated tests, measurements, and difficult diagnostics. It should not consume substantial budget replacing a quick human observation with fragile desktop automation.

When a verification procedure is useful more than once, prefer making it a small canonical repository script or documented harness rather than leaving the procedure as session-specific reasoning. Future sessions should execute the known procedure, not redesign it.

## Settled findings and operational knowledge

Treat documented environment and product findings as settled unless new evidence gives a concrete reason to reopen them. Use an already established working verification path instead of rediscovering known limitations.

A fresh Codex session must not repeat exploratory work solely because it lacks the previous session's conversational context. Repository documentation and canonical scripts are the durable memory for:

- known Windows/VM/UI automation limitations;
- established working commands and harnesses;
- known infrastructure-only failure modes;
- previously validated security/runtime paths;
- benchmark procedure and measurement assumptions;
- evidence that remains reusable for unchanged boundaries.

If a session discovers a reusable environment limitation, workaround, or verification procedure that would otherwise need to be rediscovered later, record it in the appropriate repository documentation or script before milestone closure when doing so is small and within scope.

Do not reopen a settled limitation to search for a different workaround unless:

- the active milestone actually requires evidence the established path cannot provide;
- the environment or affected implementation materially changed;
- the established path has now failed in a way that blocks required evidence.

Infrastructure or harness behavior is not automatically a product defect. Distinguish product regressions from fixture problems, automation limitations, network/tooling failures, and expected Windows behavior. Do not modify production code merely to satisfy an ambiguous harness observation without confirming real product behavior when that distinction matters.

## Verification depth

### Focused verification

This is the default after ordinary implementation work. Use targeted unit/integration tests, static inspection, focused build checks, measurements when relevant, or another narrow test that directly exercises the changed boundary.

Examples:

- search projection change: focused search/result tests;
- UI binding change: focused inspection plus user-owned manual UI smoke when a quick visual check is sufficient;
- project-reference change: build/publish plus payload/dependency inspection;
- protected-storage change: focused automated tests plus one controlled Quail-Lab security/runtime path.

### Representative regression verification

Use when a change crosses several connected components or affects a higher-risk boundary. Test the directly affected functionality plus a small number of adjacent paths. Do not mechanically expand this into every historical combination.

### Full campaign

Reserve full or high-iteration campaigns for milestones whose primary purpose requires them, release/stabilization gates, investigation of a concrete intermittent defect, or material changes to the boundary being stressed.

Do not run high iteration counts merely because a harness makes them easy. Dozens or hundreds of hotkey, keyboard, lifecycle, install/uninstall, benchmark, or similar repetitions require a concrete intermittent/cumulative failure hypothesis or an explicitly justified statistical need.

## Manual UI smoke ownership

Ordinary manual UI/UX smoke that the user can perform quickly and directly is user-owned by default. Typical examples include:

- start Quail and confirm the expected window appears;
- type a known query and confirm the expected result is visible;
- press Enter or click a result and confirm it opens;
- open Settings and confirm a changed control or label appears and behaves normally;
- perform another short visual interaction whose outcome is immediately observable without instrumentation.

The implementation agent should not spend material model/time budget creating, repairing, or repeatedly retrying WinApp, VMConnect, SendInput, desktop-session automation, or a custom harness solely to replace such a simple manual smoke.

User-owned manual smoke does **not** block the implementation agent from committing, pushing, creating/updating the PR, or handing the branch to independent QA. Record it explicitly in the handoff, for example:

`User-owned manual UI smoke: pending.`

If the active milestone requires that smoke before final acceptance, the user or independent QA completes it before the milestone is finally closed or merged.

The implementation agent should own UI/runtime verification when there is a concrete reason not to delegate it, including:

- precise timing, resource measurements, repetition, instrumentation, logs, or deterministic evidence are required;
- a concrete regression must be reproduced or debugged;
- the flow crosses a security, privilege, data-integrity, or destructive boundary requiring controlled verification;
- UI/runtime behavior is itself the primary technical subject of the milestone and automated evidence is materially more useful than a quick manual check;
- the user explicitly asks the agent to perform it.

Do not automate a simple manual smoke merely because automation is technically possible.

## Windows UI tooling

When agent-owned Windows UI automation is justified, prefer the shortest established direct path. Use WinApp when available and suitable. The Hyper-V console is not the preferred automation input channel for Quail-Lab GUI testing when reliable interactive input is required.

Existing SendInput/interactivity harnesses are focused fallbacks, not a default milestone gate. Do not start an old high-iteration lifecycle harness unless the current change or a concrete regression requires it.

## Quail-Lab

Use Quail-Lab when isolation or Windows boundaries materially improve verification, especially for:

- same-account UAC/elevated-worker behavior;
- protected `%PROGRAMDATA%` storage;
- ACL/reparse/trust-boundary checks;
- controlled Build/Rebuild/Refresh against disposable data;
- restart-sensitive or potentially destructive integration;
- installer/deployment behavior when installation itself changed.

Do not use the VM as justification for repeating unrelated full-product campaigns. Prefer one representative successful controlled path after focused automated verification.

## Physical host

Use the physical host only when the requirement cannot be established adequately in automated tests/Quail-Lab, the change affects host-specific integration, or an environment-specific regression appears. Do not repeat a previous physical-host release campaign for unchanged boundaries.

## Build, publish, and installer verification

Treat build, publish, packaging, installation, and release validation as separate evidence levels. Use the smallest applicable proof:

- normal source change: required project/solution build;
- production dependency/project-graph change: build/publish plus focused payload/dependency verification;
- packaging-script or installer-input change: build the affected package once and inspect the payload;
- installer behavior change: representative installer runtime verification;
- release/stabilization milestone: broader deployment campaign only when explicitly required.

For a new production assembly such as `Quail.FileSystem`, one successful final publish/package proof that the assembly and required runtime dependencies are present is sufficient unless packaging logic changes later.

Do not repeatedly rebuild or reinstall an unchanged package. After a failure, diagnose the cause first and rerun only after a relevant correction or to answer a concrete diagnostic question.

## Performance and benchmark verification

Performance work must use a small, repeatable, durable benchmark procedure rather than a new exploratory campaign in every session.

A performance-investigation milestone should establish and persist:

- a representative query/scenario set small enough to rerun cheaply;
- the exact measurement procedure and environment assumptions;
- the minimum repeat count needed to distinguish meaningful change from ordinary noise;
- baseline results and accepted targets/guardrails;
- any larger diagnostic scenarios that are investigation-only and are **not** part of routine regression verification.

During performance implementation:

- iterate with the smallest focused benchmark that exercises the changed hot path;
- do not rerun the full benchmark set after every code change;
- run the agreed representative performance set once on the final candidate unless a concrete regression requires another measurement;
- do not increase iteration counts merely to create more data when existing measurements already support the decision.

Later milestones such as ranking/relevance work should reuse the established performance regression set as a guardrail. They must not repeat the original performance-investigation campaign. If the regression set remains within the accepted budget, stop. If it shows a material regression, diagnose only the affected scenario first.

Performance evidence should be gathered by deterministic scripts/harnesses whenever practical so a new Codex session can execute the same procedure without rediscovering commands, warm-up behavior, timing points, or environment workarounds.

## Test infrastructure and retry budget

Prefer existing canonical scripts and established harnesses. Do not create new one-off infrastructure if focused automated tests, static inspection, direct runtime evidence, or a user-owned manual smoke can prove the requirement with less work.

Do not spend open-ended implementation-agent budget repairing verification infrastructure when the product has not produced evidence of a defect.

For an infrastructure-only failure, one obvious preparation correction and one rerun are normally sufficient. If verification still cannot produce a product PASS/FAIL because of orchestration, desktop automation, PowerShell/runtime mismatch, NuGet/network availability, artifact transfer, VM path state, timeout, or similar infrastructure failure:

- stop retrying that verification path;
- do not create another alternative harness solely to obtain the same evidence;
- record what evidence was obtained and what remains unverified;
- reuse earlier applicable PASS evidence for unchanged boundaries;
- hand off the limitation instead of continuing infrastructure diagnosis.

For a simple user-owned manual UI smoke, use an even stricter rule: if the direct automated path is unavailable or non-trivial to establish, delegate immediately rather than entering an infrastructure-debugging loop.

## Evidence reuse

Previous milestone evidence remains valid for unchanged behavior and boundaries. Reference prior evidence rather than recreating or copying large previous campaigns into the current milestone.

A new implementation session should begin by identifying which existing PASS evidence is still valid and which specific evidence was invalidated by the current change. The burden is on the new test to justify why it needs to be run, not on old evidence to be recreated automatically.

## Stop wasting verification budget

Verification has a real time and model-budget cost. Once the active milestone has sufficient evidence for the implementation-agent boundary, stop testing and hand off.

Before another expensive verification step, ask:

1. What unverified requirement or concrete risk does this test address?
2. Did a relevant implementation input change since the last applicable PASS?
3. Would failure materially change the milestone decision?

If there is no concrete answer, do not run the test.
