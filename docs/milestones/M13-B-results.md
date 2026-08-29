# M13-B short-query responsiveness remediation results

## Status

**COMPLETE — ready for independent QA.**

## Source and branch

- Branch: `m13-b-search-responsiveness-remediation`.
- Production measurement source: `5c581cd0bcba8b3476925a4dfb1567b6811bfc07`.
- The later QA follow-up updates `Invalidate()` to discard not-yet-started pending work. It does not change the approved 1,000 ms defer, the two execution lanes, or the measured primary search path; the physical-host matrix was therefore not repeated.

## Delivered scheduling policy

Quick Search now keeps one- and two-character support behind a fixed 1,000 ms trailing defer. Every input change cancels and replaces the pending short request. A query of three or more characters starts immediately.

The previous single `LatestFileSearchCoordinator` is split into two explicit bounded lanes:

- one interactive lane for queries of three or more characters;
- one short-query lane for deferred one- and two-character fallbacks.

Each lane has one worker, at most one running Core search, and one replaceable pending request. Therefore this mechanism can run at most two Core searches concurrently and does not create one task or an unbounded queue per keystroke. A new input invalidates both lanes and discards any not-yet-started pending request; it does not attempt to interrupt the running Core search. UI completion accepts only the currently valid lane completion for the current UI generation, so a completed stale short search cannot restore old results.

Core search semantics, CLI behavior, SQLite schema, literal matching, FTS5 trigram behavior, fallback behavior, ranking, candidate tiers, and result limits are unchanged.

The opt-in, privacy-safe search trace now labels coordinator records with the execution lane and flushes the Core start/completion boundaries only when tracing is enabled. Normal operation still has no trace-file I/O.

## Automated verification

- Focused M11/M13-B deferrer, coordinator, lane, stale-completion, bounded-pending, disposal, and trace tests: PASS (22 tests).
- Full Release `Quail.Core.Tests` suite: PASS (176 tests, 0 failed).
- Release `Quail.App` `win-x64` build: PASS (0 warnings, 0 errors).

The focused additions cover the approved one-second defer policy for one and two characters, timer reset, rapid short-to-interactive input without a short Core start, immediate interactive execution, independent lane start, stale-result gating, bounded pending work, invalidation removal of pending work, post-invalidation request wake-up, disposal callback suppression, and existing latest-wins coalescing behavior.

## Physical-host verification

The physical host had one complete 888,708-record managed index. The bounded WinApp/UIA batch used only privacy-safe trace data and collected five warm valid runs per group. See [measurement summary](evidence/M13-B/measurement-summary.md).

- Normal fast typed broad length-four input: 64.246 ms median input-to-first-text-render, 72.795 ms maximum. No run started short-lane Core work. Final queue wait had a 0.037 ms median and 0.060 ms maximum.
- Forced in-flight length-one to length-four: the short fallback had a 3,969.722 ms Core median, while the interactive final query rendered in 79.950 ms median with 0.037 ms final queue median (0.043 ms maximum). All five stale short completions were discarded.
- Forced in-flight length-two to length-four: short Core median was 1,722.666 ms; the interactive final query rendered in 78.497 ms median with 0.036 ms final queue median (0.050 ms maximum). All five stale short completions were discarded.
- Standalone length-one Core median was 4,074.816 ms and standalone length-two Core median was 1,658.396 ms. These remain known 0.2 behavior, not a reason to remove the supported fallback or alter its Core semantics.

## Comparison with M13-A

M13-A measured 3,059.582 ms warm broad typed input-to-render median, including 2,998.061 ms median queue wait caused by a stale length-one fallback. M13-B measures 64.246 ms for the corresponding normal fast typed group, with no short Core starts and a 0.037 ms final queue median. The forced cases demonstrate that even when the retained short fallback is actually running, the newer interactive search starts independently and its stale completion cannot update the current UI.

## Known limitations

- One- and two-character Core fallbacks remain expensive on this complete physical index. The approved 1,000 ms defer intentionally avoids running them during normal continued typing; it does not make a user who stops at a short query receive immediate results.
- There is no preemptive cancellation of a running short search. It may finish in the background, but it occupies only the short lane and cannot delay the interactive lane.
- The measurements describe this host, exact source, and current complete index. They do not control OS cache or background host activity.

## M13-C gate

M13-C packaging may begin after independent QA approves and this branch is merged. M13-B does not implement packaging or modify deployment behavior.
