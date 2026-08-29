# Environment summary

- Branch: `m10-desktop-shell`.
- Target: unpackaged WinUI 3, .NET 10, Windows 11 x64.
- Test execution: Codex terminal desktop and the existing M08 control harness.
- Release build and self-contained publish run on the physical host.
- The host can launch the self-contained application, create its real window, and validate single-instance forwarding.
- The automation token cannot inject interactive keyboard input. It also cannot perform user-driven tray interaction, so neither condition is a PASS result.
