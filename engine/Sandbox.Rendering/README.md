# Sandbox.Rendering

s&box's side of rendering: what connects the engine to [Basalt](../Basalt/README.md), the renderer, and to native's
rendering pipeline. Basalt doesn't reference this assembly; this one references Basalt.

| Folder | Responsibility |
| --- | --- |
| [Bridge](Bridge/) | Mirrors GameObject scenes into Basalt `RenderWorld`s and runs camera stages, for `r_managed_scene`. |
| [Pipeline](Pipeline/) | The layers of native's standard and 3D skybox pipelines (`NativeRenderPipeline`), for every camera Basalt doesn't render. |

[EngineHooks.cs](EngineHooks.cs) installs both at startup, from the app system. The bridge is described in the
[Basalt reference](../../docs/managed/basalt.md) and native's pipeline in the [render pipeline doc](../../docs/managed/render-pipeline.md).

Anything that knows about `SceneObject`, `SceneWorld`, cameras or native scene views belongs here, not in Basalt.
