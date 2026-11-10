# Alternative spatial-tree references — 2026-09-24

## Recommendation

**BEPU v2 is the most useful next reference for a larger redesign. Bullet's proximity insertion is the
most promising small, update-focused experiment.** Neither is a universal replacement on these workloads.
Keep the production tree until representative scene captures establish which tradeoff matters.

These experiments borrow **insertion heuristics**, not entire library implementations. Every candidate
retains our node storage, fat bounds, shrink policy, height rotations and renderer queries. In particular,
the Bullet-inspired candidate uses our balancing rather than Bullet's incremental optimizer, and the
BEPU-inspired candidate uses our rotations rather than BEPU's cost-based refinement.

## References reviewed

| Reference | Useful ideas | Fit |
|---|---|---|
| [Bullet DBVT](https://github.com/bulletphysics/bullet3/blob/master/src/BulletCollision/BroadphaseCollision/btDbvt.cpp), [selection/query helpers](https://github.com/bulletphysics/bullet3/blob/master/src/BulletCollision/BroadphaseCollision/btDbvt.h) | Cheap L1 center-distance insertion, local reinsertion, incremental optimization, per-plane containment masks | Good source of small experiments. Its full update/optimization policy differs from ours. |
| [ReactPhysics3D DynamicAABBTree](https://github.com/DanielChappuis/reactphysics3d/blob/master/src/collision/broadphase/DynamicAABBTree.cpp) | Volume-cost insertion with Box2D-style height balancing | Closest to our implementation; simple metric substitution. |
| [BEPU v2 Tree_Add](https://github.com/bepu/bepuphysics2/blob/master/BepuPhysics/Trees/Tree_Add.cs), [Tree](https://github.com/bepu/bepuphysics2/blob/master/BepuPhysics/Trees/Tree.cs), [Refit](https://github.com/bepu/bepuphysics2/blob/master/BepuPhysics/Trees/Tree_Refit.cs) | Leaf-count-weighted insertion, query nodes separated from maintenance metadata, direct bounds refitting, refinement | Especially relevant: C# and `System.Numerics`. Full layout/refit/refinement comparison would be a larger experiment. |
| [Jolt AABBTreeBuilder](https://github.com/jrouwe/JoltPhysics/blob/master/Jolt/AABBTree/AABBTreeBuilder.h) | Bulk tree construction and grouped leaves | Relevant to a separate static-world tree, not a drop-in arbitrary add/move/remove tree. |

BEPU's [binned builder](https://github.com/bepu/bepuphysics2/blob/master/BepuPhysics/Trees/Tree_BinnedBuilder.cs)
and [refinement](https://github.com/bepu/bepuphysics2/blob/master/BepuPhysics/Trees/Tree_BinnedRefine.cs) are
the next places to investigate if we pursue its complete maintenance strategy; these were not benchmarked.

## Candidates

`HeuristicTree<TPolicy>` compiles each rule into its own specialization:

- **SurfaceAreaPolicy:** control, same insertion rule as production.
- **VolumePolicy:** current greedy sibling search with volume replacing surface area, as in ReactPhysics3D.
- **ProximityPolicy:** descend to a leaf by the smaller L1 distance between box centers, as in Bullet's `Select`.
- **LeafWeightedPolicy:** descend by `mergedArea * (leafCount + 1) - oldArea * leafCount`, with ties going to
  the smaller subtree, as in BEPU's insertion rule.

No new library dependency and no production algorithm changes.

## Method

Same machine and workload definitions as [the Box3D comparison](SpatialTreeBenchmark.md): Ryzen 9 7950X,
.NET 10.0.12, Release, pinned to logical CPU 2. Six layouts, three seeds, five repetitions, rotating order,
200 bounded movement frames and query samples lasting at least 150 ms. Five frustums and 32 light spheres
per batch, including exact region and size tests. Separate default-tiered and non-tiered runs, 3,600
measurements each. All candidates verified against brute force before/after movement and churn.

The control reproduces production's heights and region-test counts. Default-tiered median build/update
times generally track it within a few percent. Disabling tiered compilation makes the generic control's
build/update path roughly 2–8% slower on several layouts despite identical topology; use this control
when interpreting small policy differences. These are not native-library or whole-frame benchmarks.

## Results

Default-tiered run. Changes relative to production; **negative is faster**. Queries are after movement.

| Layout | Policy | Build | Move 10% | Five frustums | 32 spheres |
|---|---|---:|---:|---:|---:|
| Grid 50k | Volume | -14.5% | -10.7% | -5.7% | +6.2% |
| Grid 50k | Proximity | -19.9% | -8.6% | +11.4% | -30.6% |
| Grid 50k | Leaf-weighted | -8.2% | -9.7% | +2.5% | +2.3% |
| Mixed 50k | Volume | -6.3% | -7.8% | +1.1% | +16.8% |
| Mixed 50k | Proximity | -15.2% | -14.4% | -0.7% | -7.5% |
| Mixed 50k | Leaf-weighted | -4.8% | -6.3% | -5.4% | -16.5% |
| Clusters 50k | Volume | -6.8% | -7.9% | -7.7% | +6.5% |
| Clusters 50k | Proximity | -23.4% | -16.9% | -2.8% | -16.1% |
| Clusters 50k | Leaf-weighted | -7.4% | -7.0% | -7.2% | -14.8% |
| Sorted 50k | Volume | -12.4% | -5.1% | +4.4% | +15.1% |
| Sorted 50k | Proximity | -18.9% | -13.3% | -2.1% | -4.1% |
| Sorted 50k | Leaf-weighted | -9.4% | -2.3% | +3.6% | +10.7% |
| Overlap 10k | Volume | -10.0% | -4.7% | -7.0% | -1.4% |
| Overlap 10k | Proximity | -19.4% | -15.0% | +1.5% | +6.1% |
| Overlap 10k | Leaf-weighted | -6.9% | -5.1% | -2.0% | -4.9% |

For scale, mixed-50k movement was **2.185 ms current**, **1.869 ms proximity**, **2.047 ms leaf-weighted**.
The mixed-50k sphere batch was **1.391 ms current**, **1.287 ms proximity**, **1.162 ms leaf-weighted**.

Summing median movement + moved frustums + moved spheres per seed gives these candidate/current ranges
(illustrative CPU budgets, not complete frames):

| Layout | Proximity | Leaf-weighted |
|---|---:|---:|
| Grid 10k | 0.981–1.002 | 0.989–0.997 |
| Grid 50k | 0.924–0.959 | 0.959–0.994 |
| Mixed 50k | 0.935–0.956 | 0.909–0.946 |
| Clusters 50k | 0.884–0.929 | 0.906–0.941 |
| Sorted 50k | 0.914–0.953 | 0.990–1.035 |
| Overlap 10k | 1.022–1.092 | 0.939–0.975 |

The non-tiered run broadly confirms the tradeoffs: proximity's grid frustums and overlapping sphere
queries regress, leaf-weighted sphere queries improve on mixed/clusters but regress on sorted insertion,
and volume sphere queries regress on mixed/sorted layouts. Small differences and some individual timings
shift; do not treat the percentages as hardware-independent guarantees.

All candidates stayed at height **19 or below**. Unlike the insertion-only Box3D candidate, these retain
height balancing. Spatial changes are measurable independently of timing: grid-50k moved sphere tests
fell from **64,330 to 35,022** with proximity; cluster tests fell from **42,164 to 26,466** with leaf weighting.
Conversely, proximity increased overlapping sphere tests from **424,072 to 475,550**.

## What to pursue

1. **For a small change:** compare proximity and leaf weighting on captured game-world bounds and actual
   camera/light queries. Proximity is simpler and has the strongest update savings; leaf weighting has
   a better tradeoff on these mixed-size and heavily overlapping layouts. Neither dominates.
2. **For a larger gain:** benchmark BEPU-style query-node layout and refit/refinement, or a bulk-built static
   tree alongside the dynamic tree. Those target memory traffic and update policy, not just sibling choice.
3. **For frustum-specific work:** Bullet's per-plane containment masks are worth a separate traversal
   experiment. This suite did not test them.

## Reproduce

After a Release managed build, from the repository root; the output directory must exist:

```powershell
cmd /c 'start "" /b /wait /affinity 4 dotnet engine/Benchmark/bin/Release/net10.0/Benchmark.dll --spatial-tree C:\Users\Matt\AppData\Local\Temp\opencode\spatial-tree-alternatives.json 3 200 alternatives'
cmd /c 'set DOTNET_TieredCompilation=0&& start "" /b /wait /affinity 4 dotnet engine/Benchmark/bin/Release/net10.0/Benchmark.dll --spatial-tree C:\Users\Matt\AppData\Local\Temp\opencode\spatial-tree-alternatives-notier.json 3 200 alternatives'
```

Those paths also hold the raw measurements for these runs. `--spatial-tree --summarize <json>` prints
medians, height/allocation checks, region-test counts, and per-seed budget ratios for either suite.
