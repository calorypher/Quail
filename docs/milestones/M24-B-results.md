# M24-B Results — Performance / Relevance / Resources

## Status

**ACTIVE — final physical-host performance, relevance, and resource evidence is
in progress.**

## Preparation

- Base `main`: `1a7849e1a8accc1d425b832e847505f568a7c74f` (PR #27 merge).
- `scripts/prepare-milestone.ps1` verified matching clean host and Quail-Lab
  repositories, created checkpoint `M24-B-clean`, and created branch
  `codex/m24-b-performance-relevance-resources`.
- The canonical script reported the disposable Quail-Lab data volume
  `QUAIL_LAB_DATA`.
- This evidence document is committed before final measurements so the M16
  harness can record `sourceDirty=false` for the candidate under test.

## Evidence scope

M24-B reuses the accepted M16/M17/M18 contracts and records only current
physical-host measurements, deltas, conclusions, and any evidence-driven
correction. It does not repeat M24-A deployment/lifecycle evidence or M24-C
security and release-freeze work.
