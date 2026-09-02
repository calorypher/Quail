# Quail Concurrency and Scalability Direction

## Status

**Approved long-term engineering direction.**

This document records Quail's concurrency, parallelism, cancellation, and resource-governance direction. It is an architectural principle, not a commitment to a thread-per-provider design, a dedicated scheduler framework, or maximum CPU utilization at all times.

The governing rule is:

> Independent work should execute concurrently where measurements show a latency or throughput benefit, while global scheduling prevents oversubscription and keeps interactive search responsive.

Quail should scale well with available CPU resources, but CPU utilization is a means to an end rather than a product goal by itself.

## Cross-source search concurrency

Future heterogeneous sources must not be serialized by design.

A query should conceptually fan out to the currently active sources concurrently:

```text
query
  -> Quail.Core
      -> FileSystem search
      -> Browser search
      -> Mail search
      -> Applications search
      -> Cloud search
      -> ...
  -> aggregation / ranking
  -> UI
```

Core should not impose a workflow such as `FileSystem -> Mail -> Browser -> Cloud` when those searches are independent.

The exact asynchronous contract is intentionally not frozen. `Task`, `IAsyncEnumerable`, channels, batched partial results, or another mechanism may be appropriate depending on evidence from the first heterogeneous sources.

The long-term UX direction permits progressive result availability: a fast source may contribute useful results before a slower source completes, provided aggregation, ordering, stale-result protection, and UI updates remain deterministic and understandable. The application should not block first useful results on the slowest source unless a concrete correctness requirement demands it.

## Intra-source concurrency

Each source owns the execution model appropriate to its workload behind the source-neutral Core contract.

Examples:

- a local filesystem source may use bounded CPU workers for metadata processing or parsing if profiling shows those stages parallelize well;
- a cloud/mail source should normally use asynchronous I/O rather than dedicating one OS thread to every pending network operation;
- content extraction may use bounded CPU parallelism for independent documents;
- a SQLite-backed source may keep a serial or narrowly bounded writer even while upstream reading/parsing runs concurrently if the writer is the real serialization point.

A source must not assume that it owns all logical processors. Worker counts and queue depths should be bounded and justified by measurements.

Do not introduce concurrent writes to one SQLite database, a large worker pool, or a staged reader/worker/writer pipeline merely because parallelism is theoretically possible. Measure the actual bottleneck first.

## Tasks and async rather than thread-per-provider

Quail does not adopt a permanent OS-thread-per-provider model as an architectural requirement.

In .NET, independent source operations should normally be represented as asynchronous tasks and use the runtime thread pool where appropriate. Dedicated threads are reserved for workloads that actually require thread affinity, blocking isolation, or another demonstrated reason.

I/O-bound work should release threads while waiting. CPU-bound work may use bounded parallel workers when this improves measured throughput or latency.

The architecture should express independent work and scheduling policy, not hard-code one thread for each source.

## Cancellation and supersession

Interactive search must support prompt cancellation or supersession of obsolete work.

When the user types successive states such as:

```text
q
qu
qua
quai
quail
```

Quail should avoid completing expensive obsolete searches simply because they were already queued. Newer queries should cancel, supersede, or make prior work cheaply ignorable as far down the stack as practical.

The existing generation/stale-result model remains useful, but future sources should also receive cancellation/supersession information where doing so can stop meaningful unnecessary work.

Correctness remains mandatory: cancellation must not corrupt source state, leave partial index mutations trusted as complete, or break action/result ownership.

## Global resource governance

As the number of sources grows, each source independently choosing a worker count based on total CPU capacity would create oversubscription.

Quail should therefore evolve toward small shared resource-governance mechanisms when real workloads require them. The intent is not a large generic scheduler framework; it is enough coordination to stop independent components from collectively overwhelming CPU, I/O, memory, or SQLite/storage paths.

Conceptual priority classes are:

1. **Interactive search** — highest latency priority; should preempt or receive preference over non-urgent background throughput work where practical.
2. **Incremental maintenance** — background correctness work that should remain timely but ordinarily unobtrusive.
3. **Full build/rebuild and bulk indexing** — throughput-oriented work that may use substantial CPU/resources for a bounded period when that measurably shortens completion time.
4. **Optional heavy background work**, such as future content extraction — bounded so it does not destabilize interactive use.

Do not create a global scheduler abstraction before multiple real workloads need coordinated limits. Until then, local bounded concurrency and explicit interactive-priority decisions are sufficient.

## CPU scaling and energy

A short high-utilization burst can be preferable to prolonged low parallelism when it reduces wall-clock time and lets the system return to idle sooner. This is a useful hypothesis for Quail, especially for full rebuilds and other bounded batch work, but it is not universally true.

CPU power does not scale linearly with core count, frequency, or voltage. Thermal limits, heterogeneous cores, Windows power policy, storage bottlenecks, and laptop battery state can all change the energy-optimal degree of parallelism.

Therefore Quail must not define success as `100% CPU` or `all cores busy`.

The practical target is:

> Finish eligible work quickly with efficient use of available parallelism, without sacrificing responsiveness, integrity, or energy unnecessarily.

For throughput-heavy work, measure scaling across several concurrency levels rather than assuming the largest worker count is best. A useful experiment may compare 1, 2, 4, 8, and higher bounded worker counts and record wall-clock time, CPU utilization, and where practical resource/energy implications.

A future power-aware policy may distinguish interactive work, background work on AC power, and reduced background concurrency on battery. No such policy is committed until measurements and an actual product requirement justify it.

## Relationship to Quail 0.3

This direction does not add a standalone "make Quail multithreaded" milestone to 0.3.

Concurrency is applied where a concrete workload and evidence justify it:

- **M17** may improve interactive queue/search scheduling only where required by the M16 measurements;
- **M17.5** is the first explicit opportunity to measure rebuild scaling and determine whether metadata acquisition, FTS/SQLite work, staging, or another stage benefits from bounded parallelism or a staged pipeline;
- **M19/M20** should account for priority and resource interaction between automatic filesystem maintenance and interactive search;
- the first heterogeneous-source release should validate concurrent source fan-out rather than serializing independent providers;
- later content indexing is a natural candidate for bounded CPU worker pools.

M17.5 should treat worker count, multicore scaling, and reader/worker/writer pipelines as measured hypotheses. If one worker takes much longer than a bounded multi-worker configuration, that is evidence for parallelization. If increased workers produce little or negative improvement, the investigation should identify the serial bottleneck instead of adding concurrency machinery.

## Constraints

- Preserve the source-neutral Core/source dependency direction.
- Do not expose thread-pool, worker-count, or storage implementation details through general Core search contracts unless a demonstrated cross-source requirement exists.
- Do not serialize independent sources by architectural default.
- Prefer asynchronous I/O for waiting workloads and bounded parallelism for CPU-heavy workloads.
- Prioritize interactive search over background throughput where the workloads contend.
- Bound queues and worker counts; avoid uncontrolled task creation and oversubscription.
- Propagate cancellation/supersession where it meaningfully prevents obsolete work.
- Preserve deterministic result behavior and correctness despite concurrency.
- Measure before selecting parallelism levels.
- Do not weaken durability, index integrity, privilege boundaries, or recovery correctness to obtain benchmark wins.
- Do not build a generic scheduler/provider framework merely to anticipate future sources.
