using System.Runtime.CompilerServices;

[assembly: TasksPersistOnContextReset]

[assembly: InternalsVisibleTo( "Sandbox.Test" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Unit" )]
[assembly: InternalsVisibleTo( "Sandbox.Test.Parallel" )]
[assembly: InternalsVisibleTo( "Sandbox.AppSystem" )]
[assembly: InternalsVisibleTo( "sbox-launcher" )]
