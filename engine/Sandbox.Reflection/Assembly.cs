global using Sandbox.Diagnostics;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Sandbox.Test" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "Sandbox.Hotload.Test" )]
[assembly: InternalsVisibleTo( "Sandbox.Engine" )]
[assembly: InternalsVisibleTo( "Sandbox.Tools" )]
[assembly: InternalsVisibleTo( "Sandbox.Menu" )]
[assembly: InternalsVisibleTo( "Sandbox.GameInstance" )]
[assembly: InternalsVisibleTo( "Benchmark" )]


