# Quail Deployment and Filesystem Indexing Modes

## Status

**Approved pre-implementation cross-version product/architecture direction, originally decided on 2026-08-20 and restored to the canonical roadmap on 2026-09-09.**

This direction predates the Quail 1.0 scope freeze. Restoring it does not add scope to Quail 0.3 and is not a new post-freeze product idea.

Quail 0.3 remains focused on finishing the machine/full deployment and indexing path. The per-user/limited path belongs to future roadmap work after 0.3; its exact release number is intentionally not frozen.

## Product modes

### Machine / full mode

Machine/full mode is the preferred and default Windows experience.

Intended characteristics:

- per-machine installation for all users under `C:\Program Files\Quail`;
- installation and privileged maintenance setup may require administrator approval;
- a Windows Service performs privileged filesystem index maintenance/write work;
- local NTFS volumes can use the privileged MFT/USN backend for fast whole-volume enumeration and continuous change tracking;
- machine indexes and maintenance configuration are machine-owned/protected, currently under `%PROGRAMDATA%\Quail`;
- the interactive application remains unelevated;
- Search reads compatible complete indexes directly and does not require service IPC.

This is the deployment/indexing path implemented by Quail 0.3.

### Per-user / limited mode

Per-user/limited mode is an alternative for environments where the user cannot or does not want to install privileged machine components, especially managed/work computers without administrator rights.

Intended characteristics:

- current-user installation without administrator approval, with `%LOCALAPPDATA%\Programs\Quail` as the intended installation location unless later packaging evidence selects a better conventional per-user path;
- no Quail Windows Service requirement;
- no dependency on privileged whole-volume MFT/USN enumeration or maintenance;
- by default, index only locations the current user can access, beginning with the user's profile such as `C:\Users\<username>`;
- allow explicit selection of additional accessible folders, local locations, and network shares where the selected user-mode backend can support them correctly;
- use a non-privileged initial enumeration plus user-mode change-tracking path inside `Quail.FileSystem`;
- do not freeze `FileSystemWatcher` as the architecture in advance: FileSystemWatcher, another supported Windows mechanism, or a bounded combination should be selected only after a spike/verification against correctness, rename/move behavior, missed-event recovery, network-share behavior, scale, and resource cost.

Per-user mode is intentionally limited relative to machine/full mode. It must not simulate whole-volume privilege or silently claim MFT/USN guarantees it cannot provide.

## Installation mode and indexing backend are separate concerns

The installation mode must not permanently determine which filesystem indexing backend Quail can use.

A future machine/full installation should be able to use both:

1. the privileged machine NTFS/MFT/USN backend for eligible local NTFS volumes; and
2. the non-service user-mode enumeration/change-tracking backend for targets where the machine backend is inappropriate or unavailable.

This is required for future filesystem-like targets such as SMB/network shares, and may also be useful for explicitly selected local folders. Installing Quail in full mode must not force every filesystem target through the Windows Service or through whole-volume NTFS semantics.

The likely implementation shape is more than one internal indexing/maintenance strategy inside `Quail.FileSystem`, selected according to the target and deployment capabilities. Do not generalize this into a public provider framework or universal filesystem abstraction before concrete implementations demonstrate the need.

## Search/service invariant

**Search must not architecturally depend on the Windows Service.**

The service is a privileged maintenance/write mechanism for machine-mode filesystem indexes. It is not the search engine, search broker, general Quail backend, or a prerequisite for source-neutral Core search.

The M19/M20 machine-mode architecture already establishes the important boundary: the service is the sole writer for protected machine indexes, while ordinary Search opens complete compatible indexes directly in read-only mode and uses no service IPC.

Future per-user indexes and network-share/user-mode indexes must therefore be able to participate in the same Core search path without introducing a service dependency. Source-neutral Search should care about searchable source/index state and capabilities, not about which maintenance mechanism produced that state.

## Switching between modes

The long-term product goal is to let a user move between per-user/limited and machine/full deployment without requiring a manual uninstall/reinstall workflow.

The exact migration mechanism is deliberately deferred. Later planning must resolve installer scope, shortcuts, startup registration, service registration/removal, protected versus per-user storage, ACLs, settings ownership, and stale installation artifacts safely.

A filesystem index rebuild when changing deployment/indexing mode is acceptable. Preserving every physical index database across the transition is not a product requirement if rebuilding provides a simpler and safer boundary.

Mode switching must not weaken security boundaries or leave privileged components configured after the user has intentionally moved to a non-privileged deployment.

## Relationship to network file search

Future SMB/network-share search should reuse the non-service indexing capability where that is the simplest correct implementation rather than inventing a third maintenance architecture solely because Quail is installed in machine/full mode.

Network targets do not inherit local NTFS/MFT/USN guarantees. Their enumeration, change detection, disconnect/stale behavior, reconciliation, credentials, performance, and offline semantics need separate evidence.

Whether network shares are presented as a distinct filesystem-like source, a FileSystem target type, or another bounded product concept should be decided from that implementation evidence. The invariant is narrower: they must not require the privileged local-volume service path merely because it exists.

## Roadmap timing

Quail 0.3 does **not** implement per-user/limited deployment or the general user-mode folder/network indexing backend. It finishes the machine/full path selected by M19/M20 and preserves the search/service separation needed for later work.

After 0.3, roadmap planning should explicitly schedule the per-user/limited direction. It may be suitable for 0.4 or another early post-0.3 release, but the version number must be chosen alongside the other post-0.3 priorities rather than frozen here.

The eventual implementation should be treated as a bounded vertical slice with at least:

- verified per-user installation/uninstallation without admin rights;
- user-mode enumeration and incremental/change-reconciliation correctness on representative local folders;
- evidence-based choice of change-tracking mechanism;
- search parity through the same Core/application search path;
- clear capability/limitation UX versus machine/full mode;
- safe transition between modes, with rebuild accepted where appropriate;
- representative network-share verification if network targets are included in that release.

This direction does not require Quail 0.3 to implement or simulate any of those future capabilities.