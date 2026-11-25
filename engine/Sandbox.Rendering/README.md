# Sandbox.Rendering

s&box's side of rendering: what connects the engine to [Borealis](../Borealis/README.md), the renderer. Borealis doesn't reference this assembly; this one references Borealis.

| Folder | Responsibility |
| --- | --- |
| [Bridge](Bridge/) | Mirrors GameObject scenes into Borealis `RenderWorld`s and renders cameras through Borealis on Borealis.Gpu, with their stages, in a process launched with `-borealisgpu`. |

[EngineHooks.cs](EngineHooks.cs) installs it at startup, from the app system. The bridge is described in the
[Borealis reference](../../docs/reference/borealis.md).

Anything that knows about `SceneObject`, `SceneWorld`, cameras or native scene views belongs here, not in Borealis.
