global using Sandbox;
global using System;
global using System.Collections.Generic;
global using static Sandbox.Internal.GlobalSystemNamespace;

global using Basalt;
global using Basalt.Culling;
global using Basalt.Features;
global using Basalt.Gpu;

// Basalt's Source 2 backend (Source2/), in Basalt's namespace so Basalt's types win over the engine's there.
global using Basalt.Gpu.Source2;

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Integration" )]
[assembly: InternalsVisibleTo( "Sandbox.AppSystem" )]
[assembly: InternalsVisibleTo( "scenelab" )]
