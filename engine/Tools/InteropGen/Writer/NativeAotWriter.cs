using System.Collections.Generic;
using System.Linq;

namespace Facepunch.InteropGen;

internal partial class NativeAotWriter : BaseWriter
{
	public NativeAotWriter( Definition definitions, string targetName ) : base( definitions, targetName )
	{
	}

	public override void Generate()
	{
		WriteLine("using System;");
		WriteLine("using System.Runtime.InteropServices;");
		WriteLine("using System.Runtime.CompilerServices;");
		WriteLine("using Silk.NET.Core.Native;");
		WriteLine();
		WriteLine("namespace Sbox.Engine.Emulation.Generated");
		WriteLine("{");

		GenerateExports();
		GenerateImports();
		
		WriteLine("}");
	}

}
