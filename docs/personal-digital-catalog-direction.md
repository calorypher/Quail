# Personal Digital Catalog Direction

## Status

**Approved clarification of the existing product north star.**

This document clarifies the intended character of Quail without expanding the frozen Quail 1.0 product envelope.

## Product interpretation

Quail should evolve into a **personal catalog and search system for the user's digital world**. Search is the primary product identity; launcher behavior is a convenient interaction surface around that search capability rather than the reason the product exists.

The useful abstraction is not merely "search every current storage location". Quail should increasingly answer where an object is, where it was, which source or device knows about it, what representations or related copies exist, and which actions are currently possible.

A mature result may therefore distinguish states such as:

- available locally now;
- previously known under another name or path;
- present in another indexed source;
- present on another trusted device;
- known from an offline/removable source but not currently reachable;
- represented by a related object such as an attachment, downloaded copy, or cloud version where Quail can establish that relationship safely.

This remains consistent with the existing `Source -> Object -> Searchable fragments` model. Future multi-device work may add device/location context, but should not replace source-native identity with one speculative universal identifier.

## Boundary

This clarification does not make every possible source, relationship type, platform, or distributed feature mandatory for Quail 1.0. The frozen 1.0 boundary remains governed by `docs/1.0-scope-boundary.md`.

Post-1.0 features such as peer-to-peer multi-device search, mobile peers, richer cross-device availability, and offline peer caches remain post-1.0 candidates unless separately promoted through the scope-freeze policy.
