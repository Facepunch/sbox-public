global using Sandbox;
global using System;
global using System.Collections.Generic;
global using static Sandbox.Internal.GlobalSystemNamespace;

global using Borealis;
global using Borealis.Culling;
global using Borealis.Features;
global using Borealis.Gpu;

// Borealis's Source 2 backend, in the engine (Systems/Render/Borealis/)
global using Borealis.Gpu.Source2;

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Integration" )]
[assembly: InternalsVisibleTo( "Sandbox.AppSystem" )]
