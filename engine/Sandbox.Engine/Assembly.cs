global using Sandbox;
global using Sandbox.Diagnostics;
global using Sandbox.Physics;
global using Sandbox.Tasks;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using static Sandbox.Internal.GlobalSystemNamespace;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Sandbox.Test" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "Sandbox.Hotload.Test" )]
[assembly: InternalsVisibleTo( "Sandbox.GameInstance" )]
[assembly: InternalsVisibleTo( "Sandbox.Tools" )]
[assembly: InternalsVisibleTo( "Sandbox.Menu" )]
[assembly: InternalsVisibleTo( "Sandbox.AppSystem" )]
[assembly: InternalsVisibleTo( "MenuBuild" )]
[assembly: InternalsVisibleTo( "ShaderCompiler" )]
[assembly: InternalsVisibleTo( "Sandbox.Mounting.Test" )]
[assembly: InternalsVisibleTo( "sbox-launcher" )]
[assembly: InternalsVisibleTo( "sbox-server" )]
[assembly: InternalsVisibleTo( "sbox-dev" )]
[assembly: InternalsVisibleTo( "sbox" )]
[assembly: InternalsVisibleTo( "sbox-standalone" )]
[assembly: InternalsVisibleTo( "sbox-profiler" )]
[assembly: InternalsVisibleTo( "benchmark" )]
[assembly: InternalsVisibleTo( "CreateGameCache" )]

[assembly: TasksPersistOnContextReset]

// Moved filesystem to its own assembly
[assembly: TypeForwardedTo( typeof( BaseFileSystem ) )]
[assembly: TypeForwardedTo( typeof( FileWatch ) )]

