global using Sandbox;
global using Sandbox.Engine;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using static Sandbox.Internal.GlobalGameNamespace;

using System.Runtime.CompilerServices;

[assembly: TasksPersistOnContextReset]
[assembly: InternalsVisibleTo( "Sandbox.Tools" )]
[assembly: InternalsVisibleTo( "Sandbox.AppSystem" )]
[assembly: InternalsVisibleTo( "Sandbox.Test" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
