global using Sandbox;
global using System;
global using System.Collections.Generic;
global using static Sandbox.Internal.GlobalSystemNamespace;

// Shared renderer namespaces.
global using Basalt.Culling;
global using Basalt.Features;
global using Basalt.Gpu;
global using Basalt.Shadows;

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "Basalt.Test" )]
[assembly: InternalsVisibleTo( "Basalt.Backend" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Integration" )]
[assembly: InternalsVisibleTo( "Sandbox.Engine" )]
[assembly: InternalsVisibleTo( "Sandbox.Rendering" )]
[assembly: InternalsVisibleTo( "scenelab" )]
[assembly: InternalsVisibleTo( "Benchmark" )]
