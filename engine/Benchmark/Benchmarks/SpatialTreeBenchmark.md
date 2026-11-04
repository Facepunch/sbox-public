# Spatial tree comparison — 2026-09-24

Follow-up: [Bullet, ReactPhysics3D and BEPU-inspired insertion experiments](SpatialTreeAlternatives.md).

## Verdict

Keep the current `SpatialTree` as the simplicity-first default. Box3D-style insertion and cost rotations
improve spatial grouping, particularly for local sphere queries, but are not an across-the-board win.
Building and maintaining the tree costs more. Skipping rotations on updates recovers much of that cost
but allows substantial height growth without rebuilding.

The candidates live only in the benchmark project. This comparison tests the insertion/rotation approach,
**not the complete Box3D broadphase**: no periodic partial rebuilds, category masks or collision-pair tracking.

## Method

- Ryzen 9 7950X, Windows 10.0.26200, .NET 10.0.12, Release build.
- Pinned to logical processor 2 (`/affinity 4`). Separate runs with default tiered JIT and
  `DOTNET_TieredCompilation=0`; both support the same general conclusion.
- Six deterministic layouts: 10k/50k grids, 50k mixed sizes (including large objects), 50k clustered,
  50k X-sorted insertion, and 10k heavily overlapping bounds.
- Three random seeds, five repetitions per seed, rotating implementation order: 15 samples per row.
  Grid geometry is fixed; seeds vary its light queries.
- Three untimed warm-up runs per implementation. Query samples repeat for at least 150 ms.
- Identical 48-byte node fields, allocation strategy, padding/shrink hysteresis and traversal semantics.
  Actual production `SpatialTree`, `ViewFrustum`, `SizeCull`, `FrustumQuery`, and `SphereQuery` are used.
- Five frustums per query batch: one perspective camera and four orthographic volumes, with root-camera
  size culling. Sphere batches contain 32 local-light regions. Both include exact per-object region and
  size tests. Sphere results are not subsequently expanded into six cube-face culls.
- Update workload moves the same 10% of objects for 200 frames, in bounded sinusoidal paths. Timings
  include position generation. A separate batch nudges every object within its padding. Churn removes
  and reinserts 10% after movement.
- All three implementations' final visible sets are checked against brute force for every query,
  before movement, after movement and after churn. Structural invariants are also checked.
- No graphics/native engine, frame preparation, draw submission, parallel shadow jobs or Tracy capture.
  These are isolated CPU costs, not FPS measurements or captured game-world workloads.

Candidates:

1. **All rotations:** Box3D-style best-so-far sibling search with lower-bound pruning and centroid
   tie-breaking; surface-area-cost rotations after inserts, moves and removals.
2. **Insert rotations:** same search and rotation, but only rotates on `Add`, matching Box3D's cheaper
   move/remove policy. Unlike Box3D's full system, this experiment does not rebuild periodically.

## Results

Default tiered JIT, medians in milliseconds. The cheaper **insert-rotations** candidate is shown below.
Each cell is **current → candidate**. Frustum/sphere rows here are after 200 movement frames.

| Layout | Build | Move 10%, per frame | Five frustums | 32 spheres | Remove/reinsert 10% |
|---|---:|---:|---:|---:|---:|
| Grid 10k | 2.158 → 2.606 | 0.282 → 0.273 | 0.414 → 0.391 | 0.147 → 0.139 | 0.288 → 0.382 |
| Grid 50k | 11.145 → 14.033 | 1.752 → 1.855 | 2.462 → 2.223 | 1.188 → 0.819 | 1.648 → 2.253 |
| Mixed 50k | 16.300 → 26.740 | 2.120 → 2.602 | 3.565 → 3.510 | 1.411 → 1.104 | 2.396 → 3.557 |
| Clusters 50k | 17.617 → 26.864 | 2.107 → 2.908 | 2.944 → 3.023 | 1.440 → 1.245 | 2.306 → 3.854 |
| Sorted 50k | 13.828 → 21.290 | 1.818 → 2.245 | 3.205 → 3.151 | 0.941 → 0.806 | 1.878 → 3.465 |
| Overlap 10k | 2.726 → 4.723 | 0.323 → 0.708 | 0.819 → 0.842 | 6.902 → 7.344 | 0.346 → 0.624 |

For a synthetic budget of one update batch + five frustums + 32 spheres, the candidate/current ratio
across the three seeds was:

| Layout | Candidate/current range | Interpretation |
|---|---:|---|
| Grid 10k | 0.940–0.960 | Small candidate win |
| Grid 50k | 0.897–0.912 | Candidate wins about 9–10% |
| Mixed 50k | 0.993–1.027 | Essentially a tie |
| Clusters 50k | 1.068–1.131 | Current wins |
| Sorted 50k | 1.015–1.050 | Slight current advantage |
| Overlap 10k | 1.079–1.108 | Current wins |

These sums are illustrative budgets from independently timed operations, not measured complete frames.
The number of moving objects and queried lights determines the actual break-even point.

The all-rotations candidate's movement cost was 64–116% higher than current with normal tiering, and
its churn cost 74–132% higher. It did not earn its maintenance cost in this query mix.

The spatial improvement is real: median region tests for the moved sphere batch fell from 69,530 to
54,530 on mixed bounds, and 42,164 to 21,586 on clusters (insert-only variant). Fewer tests do not always
mean less elapsed time: traversal, result output, exact tests, tree depth and memory locality still matter.

Maximum observed heights:

| Layout | Current | All rotations | Insert rotations |
|---|---:|---:|---:|
| Grid 10k | 15 | 19 | 18 |
| Grid 50k | 18 | 23 | 27 |
| Mixed 50k | 19 | 26 | 56 |
| Clusters 50k | 19 | 25 | 50 |
| Sorted 50k | 19 | 24 | 26 |
| Overlap 10k | 16 | 27 | 167 |

The height growth is the clearest reason not to transplant Box3D's move policy without its rebuild
strategy. A query-heavy, mostly static tree remains a plausible use for this approach.

Treat small timing differences as noise. Identical initial candidate topologies still show timing
variation between wrappers/runs; the non-tiered run also shifts absolute costs. Allocation telemetry
contains occasional small nonzero query samples in both current and candidate runs; this benchmark
does not establish a stronger zero-allocation guarantee than the existing renderer allocation test.

## Reproduce

Build the managed solution in Release using the renderer's documented build command, then run from
the repository root (PowerShell). The output directory must already exist.

```powershell
cmd /c 'start "" /b /wait /affinity 4 dotnet engine/Benchmark/bin/Release/net10.0/Benchmark.dll --spatial-tree C:\Users\Matt\AppData\Local\Temp\opencode\spatial-tree-pinned-tiered.json 3 200'
cmd /c 'set DOTNET_TieredCompilation=0&& start "" /b /wait /affinity 4 dotnet engine/Benchmark/bin/Release/net10.0/Benchmark.dll --spatial-tree C:\Users\Matt\AppData\Local\Temp\opencode\spatial-tree-pinned.json 3 200'
```

Arguments are output JSON path, seed count, movement-frame count. Each file contains 2,160 measurements.
These are also the raw output locations for the recorded runs. Print their summaries without rerunning:

```powershell
dotnet engine/Benchmark/bin/Release/net10.0/Benchmark.dll --spatial-tree --summarize C:\Users\Matt\AppData\Local\Temp\opencode\spatial-tree-pinned-tiered.json
```

Verification: full Release solution build with warnings as errors passed; all 58 scene-renderer unit
tests passed; brute-force checks passed for both candidates throughout both final benchmark runs.
