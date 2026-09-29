global using Sandbox;
global using System;
global using System.Collections.Generic;
global using static Sandbox.Internal.GlobalSystemNamespace;

// Shared renderer namespaces.
global using Sandbox.SceneRenderer.Culling;
global using Sandbox.SceneRenderer.Features;
global using Sandbox.SceneRenderer.Gpu;
global using Sandbox.SceneRenderer.Shadows;

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "scenelab" )]
[assembly: InternalsVisibleTo( "Benchmark" )]
