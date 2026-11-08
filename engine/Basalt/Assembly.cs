global using Sandbox;
global using System;
global using System.Collections.Generic;
global using static Sandbox.Internal.GlobalSystemNamespace;

// Shared renderer namespaces.
global using Basalt.Culling;
global using Basalt.Features;
global using Basalt.Gpu;
global using Basalt.Shadows;

// The Source 2 backend, until it moves out at the flip.
global using Basalt.Gpu.Source2;

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Integration" )]
[assembly: InternalsVisibleTo( "Sandbox.Rendering" )]
[assembly: InternalsVisibleTo( "scenelab" )]
[assembly: InternalsVisibleTo( "Benchmark" )]
