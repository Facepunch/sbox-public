global using Sandbox.Diagnostics;
global using System;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.Linq;
global using System.Text;
global using System.Threading.Tasks;
global using static Sandbox.Internal.GlobalSystemNamespace;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo( "Sandbox.Test" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "Sandbox.Tools" )]
[assembly: InternalsVisibleTo( "Sandbox.Menu" )]
[assembly: InternalsVisibleTo( "Sandbox.GameInstance" )]
[assembly: InternalsVisibleTo( "Sandbox.Engine" )]
[assembly: InternalsVisibleTo( "Sandbox.Compiling" )]
[assembly: InternalsVisibleTo( "Sandbox.Compiling.Test" )]
