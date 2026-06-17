using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public static int Value = 0;

	public int Main( StringWriter output )
	{
		output.Write( "Hello World" );

		return Value++;
	}
}
