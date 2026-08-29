# M13-A search performance trace contract

`SearchPerformanceTrace` is an internal, opt-in JSONL trace. It is inactive unless Quail is started with the internal `--search-performance-trace` option, so normal search has no trace-file I/O or process-metrics sampling.

Every record uses one process-local `Stopwatch` origin and contains only:

- a random process-local run ID and UI/search generation IDs;
- a fixed stage name and monotonic relative time;
- query length, result count, result index, and bounded duration fields;
- index count, record count, database byte count, and unavailable-index count at session start;
- process CPU time and working set only at session start and first textual-result render.

The trace does not contain query text, file or directory names, paths, source identities, usernames, machine identifiers, raw UIA/test-pipe payloads, or remote telemetry. Existing test event pipes remain content-bearing QA interfaces and are not used as M13-A trace output.

The trace stages cover input observation; one/two-character defer; request enqueue; worker dequeue; Core start/end with queue and Core duration; completion/UI dispatch; mapping; collection apply; selection/scroll; freshness/status; first textual render; and asynchronous icon timing. Textual-render and icon timing are sampled independently: uncached icon work may complete after textual render, while cached icon work may complete before the recorded render boundary. This still makes icon contribution separable from visible text latency.

The internal plumbing is a suitable low-level basis for a later explicitly user-triggered diagnostics feature, but M13-A adds no UI, retention/export policy, support bundle, or telemetry workflow.
