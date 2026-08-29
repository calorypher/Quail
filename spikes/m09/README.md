# M09 deployment fixture

This directory publishes and packages the existing M08 WinUI fixture without modifying it.

`publish-m09.ps1` builds one Release x64 folder publish for `self-contained` or `framework-dependent`. `build-installers.ps1` compiles the matching Inno Setup fixture and injects pinned prerequisite URLs and SHA-256 values from an ignored local cache. The resulting installers are experimental evidence only; they are not Quail 0.1 artifacts and do not create the future production `Quail.exe`.

The framework-dependent installer deliberately owns no shared runtime. Its prerequisite helpers run before the `[Files]` payload copy, so a missing/offline dependency produces an early controlled failure without a partial Quail installation.
