# QA Automation foundation results

## Status

**DEFERRED after feasibility.** WinApp CLI 0.6.0/UIA works in an interactive Quail-Lab VMConnect session, but unattended SSH-to-interactive-desktop execution was not made reliable. The spike is sufficient for the 0.2 decision and does not block 0.2. No GUI automation, accessibility-target, application, deployment, or product change was retained.

## Prepared baseline

- Host and Quail-Lab source: `9a3492b0e89c227e9433fe929c7335f654189aaf`.
- Host branch created by the canonical preparation procedure: `qa-winapp-foundation`.
- Quail-Lab checkpoint created by the canonical preparation procedure: `QA-Automation-clean`.
- Quail-Lab data volume: healthy NTFS volume `QUAIL_LAB_DATA`.
- Canonical preparation command: `scripts\prepare-milestone.ps1 -Milestone QA-Automation -CheckpointName QA-Automation-clean -BranchName qa-winapp-foundation -VmUser quailadmin -VmRepositoryPath C:\Projects\Quail`.

## Resolved prerequisite history

The guest did not have a `winapp` command. The required official installation was attempted over the existing canonical Quail-Lab SSH/encoded-PowerShell transport:

```powershell
winget install Microsoft.winappcli --source winget
```

Winget found `Windows App Development CLI [Microsoft.WinAppCli] Version 0.6.0` and reported a successfully verified installer hash, then failed while starting the installer:

```text
Installer failed with exit code: 0x80070002 : The system cannot find the file specified.
```

The user then completed the same approved Winget installation manually as `quailadmin` in the interactive Quail-Lab VMConnect session. The guest now reports:

```text
Windows App Development CLI / WinApp CLI 0.6.0
winapp --version -> 0.6.0
```

The existing SSH automation context resolves `winapp` at `C:\Users\quailadmin\AppData\Local\Microsoft\WindowsApps\winapp.exe`. `winapp --help` reports the `UI Automation` section, and `winapp ui --help` exposes `inspect`, `search`, `get-property`, `get-value`, `set-value`, `invoke`, `screenshot`, and `wait-for`.

## Execution-model blocker

The execution-model check used a fresh self-contained Release publish from source `54248dfd60247892b6726569f70cdf5b61dc055f` (application code remains the validated `9a3492b0e89c227e9433fe929c7335f654189aaf` baseline). The exact ZIP transferred through the canonical SSH/SCP `HostKeyAlias` path had SHA-256 `8ae805377d5831e70e520d01b22dc60dccb3df4dfb54bfa3dfb5d57e7ac4a919`.

Quail was started with `--m10-show-on-start` from the SSH PowerShell session. While its process was still alive and its diagnostics had reached `Program startup.` and `Settings defaulted because no config exists.`, the following UIA commands both succeeded technically but returned no windows:

```text
winapp ui list-windows -a <quail-process-id> --json  -> []
winapp ui inspect -a <quail-process-id> --interactive --json -> { "windows": [] }
```

The guest's `quser` command reported `No User exists for *`, confirming that there was no active interactive desktop at the time of the SSH automation check. The process was stopped only by its own test cleanup after the observation. This does not demonstrate a WinUI 3 or WinApp CLI incompatibility; it demonstrates that the current host-to-guest SSH context is isolated from an interactive desktop.

No alternative installation channel was used. In particular, this milestone did not run `winapp init`, install the npm package, add WinApp CLI to Quail, create an inter-session broker or scheduled-task bridge, use synthetic pixel automation, or change Quail deployment.

## Approved transient Task Scheduler proof

The session-isolation finding was followed by an explicit approval for one narrow transient task model. Before every attempt, Quail-Lab reported an active `quailadmin` VMConnect session with ID `3`. The proof used the ScheduledTasks PowerShell API with the local `-LogonType Interactive` enum, whose registered task XML was checked for the equivalent `<LogonType>InteractiveToken</LogonType>`, and `-RunLevel Limited`.

The task action was limited to a copied repo-owned proof script. It was intended to write an atomic JSON result with the current session ID, Windows identity, integrity level, and timestamp. No password, `RunLevel Highest`, credential storage, service, autorun, PsExec, arbitrary command bridge, pixel automation, UAC automation, or Quail privilege-boundary change was used.

The proof did not pass: after two result timeouts, a later wrapper attempt left exactly one `Quail-QA-Smoke-<guid>` task. The task was audited, stopped and removed by its exact generated name; a final read-only audit returned `RemainingTasks: 0`. The wrapper therefore did not satisfy the required fail-closed cleanup contract and was discarded instead of being committed.

This proof result is sufficient for the feasibility decision. Unattended GUI automation through an SSH-to-interactive-desktop bridge is deferred and is not a Quail 0.2 requirement. Further debugging of this transient infrastructure is outside this milestone. WinApp CLI remains available for later selective, user-triggered smoke from the same active VMConnect session; any such future smoke should remain UIA-based and must not require pixel automation.

## Final decision and scope boundary

No next implementation step is required for this spike. Quail 0.2 may rely on normal automated tests plus manual VMConnect GUI smoke. A later user-triggered guest-side WinApp smoke may be added selectively if it yields real QA savings; it is deferred and outside this slice. No persistent broker, broader scheduled-task facility, PsExec-style mechanism, or pixel-input fallback is in scope.

The ignored diagnostic logs are under `.quail-tooling\qa-winapp-install-20260825-190936*.log`, `.quail-tooling\qa-winapp-context-20260825-191728*.log`, `.quail-tooling\qa-execution-model-20260825-191926*.log`, `.quail-tooling\qa-execution-model-live-20260825-192218*.log`, `.quail-tooling\qa-interactive-session-check-20260825-193350*.log`, `.quail-tooling\qa-interactive-task-proof-20260825-193535*.log`, `.quail-tooling\qa-interactive-task-proof-20260825-193629*.log`, and `.quail-tooling\qa-task-cleanup-audit-20260825-194552*.log` in the preparation workspace.
