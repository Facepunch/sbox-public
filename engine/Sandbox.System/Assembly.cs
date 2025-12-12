global using System;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.Linq;
global using System.Text;
global using System.Text.Json;
global using static Sandbox.Internal.GlobalSystemNamespace;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Sandbox.Test" )]
[assembly: InternalsVisibleTo( "Sandbox.Hotload.Test" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "Benchmark" )]

[assembly: InternalsVisibleTo( "Sandbox.Access" )]
[assembly: InternalsVisibleTo( "Sandbox.Engine" )]
[assembly: InternalsVisibleTo( "Sandbox.Tools" )]
[assembly: InternalsVisibleTo( "Sandbox.Bind" )]
[assembly: InternalsVisibleTo( "Sandbox.Reflection" )]
[assembly: InternalsVisibleTo( "Sandbox.Menu" )]
[assembly: InternalsVisibleTo( "Sandbox.GameInstance" )]
[assembly: InternalsVisibleTo( "Facepunch.Interopgen" )]
[assembly: InternalsVisibleTo( "Sandbox.AppSystem" )]
[assembly: InternalsVisibleTo( "Sbox-Server" )]

[assembly: TasksPersistOnContextReset]
