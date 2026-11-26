# Sandbox.Rendering

s&box's side of rendering: what connects the engine to [Borealis](../Borealis/README.md), the renderer. Borealis doesn't reference this assembly; this one references Borealis.

| Folder | Responsibility |
| --- | --- |
| [Bridge](Bridge/) | Mirrors managed and native-authored tool objects into Borealis `RenderWorld`s. Resolves camera, capture, fog and sky settings per view, and runs camera stages on Borealis.Gpu. |

[EngineHooks.cs](EngineHooks.cs) installs it at startup, from the app system. The bridge is described in the
[Borealis reference](../../docs/reference/borealis.md).

Processes without a Borealis.Gpu device do not render scene cameras. Native scene objects created by editor tools are still read by the bridge; they are not a separate renderer.

Anything that knows about `SceneObject`, `SceneWorld`, cameras or native scene views belongs here, not in Borealis.
