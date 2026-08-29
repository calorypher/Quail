# M13-C verification summary

All values below are aggregate and use only controlled lab sentinels.

| Scenario | Result | Downloads | Payload/data result |
|---|---|---:|---|
| Final natural prerequisite state | PASS | 135,155,336 B | .NET Desktop 10.0.11 present; incomplete older Windows App Runtime registrations and absent VC++ were rejected. The final setup downloaded and SHA-256-verified only VC++ x64 (18,731,856 B) and Windows App Runtime (116,423,480 B) before Quail copy. |
| Final offline, prerequisites present | PASS | 0 B | Outbound traffic was blocked only for the final setup process; installation succeeded and both executables existed. |
| Final real `v0.1.0` upgrade | PASS | VC++ + Windows App Runtime only when absent | One 0.2 uninstall entry; GUI/CLI present; obsolete `coreclr.dll` absent. The final setup SHA-256 was verified before transfer. |
| Same-version reinstall | PASS | 0 B | Controlled LocalAppData, ProgramData Indexes, and external index-class sentinels retained their SHA-256. |
| Uninstall | PASS | 0 B | 73 installer-owned files removed; GUI, CLI, Start Menu link and PATH entry removed; `{app}` remained empty; sentinels and Microsoft runtimes retained. |
| Clean 0.2 install, prerequisites present | PASS | 0 B | 73 files / 91,138,646 B installed; one machine PATH entry and the expected Start Menu link were present. |
| Final detector fixture seam | PASS | n/a | Six deterministic cases passed: required stable 2.4.0, newer stable 2.x, old/missing DDLM rejection, other-user-only registration rejection, and preview/experimental rejection. |
| Final installed GUI/CLI smoke | PASS | 0 B | User confirmed the installed final GUI works; installed CLI reported `quail 0.2.0`. |

The source-controlled prerequisite inputs, direct Microsoft URLs, SHA-256 values, and version metadata are recorded in `packaging/prerequisite-pins.json` and summarized in `M13-C-results.md`. The retained real 0.1.0 installer has SHA-256 `b39894a0ad807391af6abc1874ae02ff6e3e0c4133eab51e4bcd5f4460ab66a4`; the final 0.2 installer (source `c6ceba94605a3cbfd85972d9be945e7a181ca470`) is 23,664,414 bytes with SHA-256 `a621be3eeb85019f422dd9e763a90e0fb0836ff19b13a3aad42e2f59c49c2e2f`.
