# Post-1.0 Mobile Direction

## Status

**Post-1.0 product-direction candidate.**

This document refines the mobile part of the existing multi-device/P2P idea. It does not change the frozen Quail 1.0 scope.

## Why mobile becomes useful

A mobile Quail client is most valuable once Quail can search more than the local device. Its primary role should not be to imitate a desktop filesystem indexer on a phone, but to provide a portable search surface for the user's wider personal digital catalog.

A mobile peer may combine:

- local sources the mobile platform allows Quail to access safely;
- remote results from trusted Quail peers such as desktop PCs or laptops;
- cloud/mail/browser or other account-backed sources already represented in Quail;
- bounded last-known metadata for temporarily offline peers where the later P2P design supports it.

A useful result may therefore show that the same or related object exists on the phone, one or more PCs, and another source such as mail or cloud storage, while preserving source/device availability context.

## Platform order

**Android is the preferred first mobile investigation target.** It should be evaluated when there is a concrete device/use case and after the shared Core/application boundaries are mature enough to make reuse worthwhile.

Android should not be assumed to provide unrestricted access to every app's private data. The expected model is platform-supported local sources plus remote/shared Quail sources, not a global bypass of the mobile platform's sandbox/storage model.

**iOS is a later candidate rather than an initial mobile requirement.** Its value would primarily be as a Quail search client/peer and an interface to explicitly accessible local data and remote sources. Do not make iOS support a prerequisite for the broader mobile direction.

The project should not acquire platform-specific build infrastructure solely to reserve theoretical support. Revisit iOS when there is a real use case and a practical Apple build/test environment.

## Architectural guardrails

- Do not create a speculative "mobile Core" before a real mobile implementation exists.
- Reuse source-neutral Core/application behavior where it is genuinely portable; keep platform-specific storage, lifecycle, background-execution, UI, permissions, and local-source integration explicit.
- A mobile peer may intentionally expose fewer local source capabilities than a desktop peer.
- Mobile storage, background work, network use, and battery impact require their own budgets.
- Mobile participation should not require replicating every desktop index in full; federated search and bounded metadata caches remain valid options for later evaluation.

## Relationship to the P2P direction

This direction is complementary to `docs/post-1.0-ideas.md` and its peer-to-peer multi-device concept. The eventual ordering among Linux support, P2P foundation, Android, and iOS remains unfrozen and should be chosen from implementation evidence and actual device use cases.
