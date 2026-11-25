global using Sandbox;
global using System;
global using System.Collections.Generic;
global using static Sandbox.Internal.GlobalSystemNamespace;

// Shared renderer namespaces.
global using Borealis.Culling;
global using Borealis.Features;
global using Borealis.Gpu;
global using Borealis.Shadows;

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "Borealis.Test" )]
[assembly: InternalsVisibleTo( "Borealis.Backend" )]
[assembly: InternalsVisibleTo( "Borealis.Gpu.Test" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Integration" )]
[assembly: InternalsVisibleTo( "Sandbox.Engine" )]
[assembly: InternalsVisibleTo( "Sandbox.Rendering" )]
[assembly: InternalsVisibleTo( "borealislab" )]
[assembly: InternalsVisibleTo( "Benchmark" )]
