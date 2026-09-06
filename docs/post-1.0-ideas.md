# Quail Post-1.0 Ideas

## Status

**Parking lot for product ideas that are explicitly outside the frozen Quail 1.0 scope by default.**

This file exists so new ideas can be preserved without continually moving the Quail 1.0 release goal.

The governing scope policy is `docs/1.0-scope-boundary.md`.

## Rules

- New product ideas proposed after the 2026-09-06 1.0 scope freeze belong here by default.
- An entry here is not a commitment, priority, milestone, or version assignment.
- Ideas may be refined, combined, rejected, or deleted from this parking lot later without affecting the pre-1.0 roadmap.
- Do not move an idea into pre-1.0 scope merely because it is useful, interesting, or architecturally compatible.
- Moving a post-freeze idea into pre-1.0 requires an explicit cross-version decision that it is genuinely necessary to satisfy the already-frozen 1.0 product definition or an unavoidable correctness/security/compatibility prerequisite.
- Technical discoveries that are required to implement existing pre-1.0 scope are not "new product ideas" and may still be handled inside the appropriate active version.

## Candidates

### Peer-to-peer LAN search mesh / multi-device Quail

**Category:** networking, distributed search, device identity, remote actions, mobile.

Working concept: Quail instances on devices in the same trusted local network can discover one another and cooperate directly without a mandatory central server or Quail-operated cloud service.

Possible user experience:

- a Quail instance discovers other Quail devices on the LAN;
- trusted devices establish authenticated membership in the same user-defined Quail group;
- devices exchange enough index/search information to make remote-device objects searchable, either by synchronizing selected index state, exposing remote indexes through a federated search protocol, or using another bounded peer-to-peer model selected by later evidence;
- one query can return an object present on multiple devices and distinguish the locations while preserving object identity/history where Quail can prove that they represent the same underlying content/object;
- example: searching for `plik.docx` on PC1 may show the current copy on PC2, the related/current or historical copy on PC1, and later the same logical object from another indexed source such as Gmail;
- remote file results can expose actions such as transferring/fetching the file from the peer and opening it locally, conceptually similar to a lightweight LAN file-transfer workflow;
- the same model could make a mobile Quail client useful: a search result may exist on a phone, one or more PCs, and cloud/mail sources while appearing in one object-centric result view.

The initial discovery sketch is LAN broadcast/multicast discovery with peer responses, but discovery transport is not frozen. Likewise, the initial idea of matching shared passwords is only a concept for group membership, not an approved security design. Any real implementation should use authenticated device/group membership with explicit pairing/trust, strong keys/credentials, revocation, and encrypted peer communication rather than relying on a plaintext or replayable shared password protocol.

Important design questions for later investigation:

- federated live search versus replicated/synchronized remote index metadata, or a hybrid;
- device identity, pairing, trust groups, key rotation, revocation, and compromise recovery;
- privacy boundaries: which sources, objects, metadata, history, and content each peer is allowed to expose;
- behavior when peers are offline, asleep, roaming, or reachable through multiple interfaces;
- duplicate/content identity across machines versus merely similar copies;
- conflict semantics when the same object has diverged on multiple devices;
- remote actions and file transfer authorization, integrity checking, resume, and destination policy;
- whether LAN-only remains the product boundary or later trusted peer connectivity may cross routed/private networks;
- mobile platform constraints, background execution, local indexing capabilities, and battery/network cost;
- how distributed results participate in ranking without making interactive search depend on the slowest peer;
- how this feature interacts with Quail's local-first privacy model and first-party source modularity.

This idea is explicitly **post-1.0**. It does not change the frozen Quail 1.0 target or the current pre-1.0 roadmap.
