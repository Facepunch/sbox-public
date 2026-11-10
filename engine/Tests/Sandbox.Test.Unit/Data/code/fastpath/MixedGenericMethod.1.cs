using System;
using System.IO;

namespace TestPackage;

public class GenericClass<T1>
{
	public string Greet<T2>( T1 arg1, T2 arg2 )
	{
		return $"Hello {arg1} {arg2}";
	}
}

public class Program : CompilingTests.IProgram
{
	public int Main( StringWriter output )
	{
		var inst = new GenericClass<int>();

		output.Write( inst.Greet( 53, "World" ) );

		return 0;
	}
}
